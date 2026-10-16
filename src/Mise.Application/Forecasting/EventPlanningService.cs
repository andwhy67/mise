using FluentValidation;
using FluentValidation.Results;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Mise.Application.Abstractions;
using Mise.Application.Common;
using Mise.Domain.Abstractions;
using Mise.Domain.Common;
using Mise.Domain.Forecasting;
using Mise.Domain.Orders;
using Mise.Domain.Stock;

namespace Mise.Application.Forecasting;

public sealed record EventItemRequest(Guid DishId, int Portions);

/// <param name="At">Когда событие (банкет, большой заказ): в будущем, не дальше <see cref="EventPlanningService.MaxDaysAhead"/> дней.</param>
public sealed record EventPlanRequest(IReadOnlyList<EventItemRequest> Items, DateTimeOffset At);

public sealed record EventDishDto(Guid DishId, string Name, int Portions, decimal Price);

public sealed record EventLineDto(
    Guid ProductId,
    string ProductName,
    MeasureUnit Unit,
    decimal Need,
    decimal AvailableAtEvent,
    decimal Shortage,
    decimal BuyQty,
    string? Supplier,
    decimal RescuedQty,
    decimal? RescuedCost);

public sealed record EventStopDto(Guid DishId, string Name, int PortionsBefore);

/// <param name="Revenue">Выручка события по ценам меню, ₽.</param>
/// <param name="RescuedCost">Сколько продуктов (₽) событие заберёт из того, что без него истекло бы неиспользованным.</param>
public sealed record EventPlanDto(
    DateTimeOffset GeneratedAt,
    DateTimeOffset At,
    bool Feasible,
    decimal Revenue,
    decimal RescuedCost,
    IReadOnlyList<EventDishDto> Dishes,
    IReadOnlyList<EventLineDto> Lines,
    IReadOnlyList<EventStopDto> StopsAfter);

internal sealed class EventPlanRequestValidator : AbstractValidator<EventPlanRequest>
{
    public const int MaxItems = 30;
    public const int MaxPortions = 500;

    public EventPlanRequestValidator()
    {
        RuleFor(x => x.Items).NotEmpty()
            .Must(i => i.Count <= MaxItems).WithMessage($"Не больше {MaxItems} блюд.")
            .Must(i => i.Select(x => x.DishId).Distinct().Count() == i.Count).WithMessage("Блюдо указано дважды.");
        RuleForEach(x => x.Items).ChildRules(i =>
        {
            i.RuleFor(x => x.DishId).NotEmpty();
            i.RuleFor(x => x.Portions).InclusiveBetween(1, MaxPortions);
        });
    }
}

/// <summary>
/// «Что если»: хватит ли продуктов на банкет к его началу с учётом обычного потока гостей и истечения партий,
/// что докупить (до фасовки), что спасётся от порчи и какие блюда после банкета уйдут в стоп. Ничего не резервирует.
/// </summary>
public sealed class EventPlanningService(
    IMiseDbContext db,
    IClock clock,
    IConsumptionAggregator aggregator,
    ConsumptionRates rates,
    IOptions<ForecastOptions> options,
    IValidator<EventPlanRequest> validator)
{
    public const int MaxDaysAhead = 7;

    public async Task<EventPlanDto> PlanAsync(EventPlanRequest request, CancellationToken ct)
    {
        await validator.EnsureValidAsync(request, ct);
        await aggregator.RefreshAsync(ct);
        var now = clock.UtcNow;
        var at = request.At.ToUniversalTime();
        if (at <= now || at > now.AddDays(MaxDaysAhead))
            throw new[] { new ValidationFailure(nameof(request.At), $"Событие — в будущем, не дальше {MaxDaysAhead} дней.") }.ToValidationException();

        var dishes = await db.Dishes.AsNoTracking().Include(d => d.Ingredients).ToListAsync(ct);
        var byId = dishes.ToDictionary(d => d.Id);
        var unknown = request.Items.Select((item, index) => (item, index)).Where(x => !byId.ContainsKey(x.item.DishId))
            .Select(x => new ValidationFailure($"Items[{x.index}].DishId", "Блюдо не найдено.")).ToList();
        if (unknown.Count > 0)
            throw unknown.ToValidationException();

        var demand = OrderDemand.Aggregate(request.Items.Select(i => (byId[i.DishId], i.Portions)));
        var products = await db.Products.AsNoTracking().ToListAsync(ct);
        var batches = (await db.Batches.AsNoTracking()
                .Where(b => b.Status == BatchStatus.Active && b.RemainingQty > 0 && b.ExpiresAt > now)
                .Select(b => new { b.ProductId, Batch = new ForecastBatch(b.Id, b.ExpiresAt, b.RemainingQty) })
                .ToListAsync(ct))
            .ToLookup(b => b.ProductId, b => b.Batch);
        var productRates = await rates.LoadAsync(now, ct);

        var model = products.ToDictionary(p => p.Id, p => new EventProduct(p.Id, p.Name, p.Unit, p.PackSize, p.ReferenceUnitCost,
            batches[p.Id].ToList(), productRates.GetValueOrDefault(p.Id) ?? ConsumptionRate.None));
        var menu = dishes.Where(d => d.IsActive)
            .Select(d => new ForecastDish(d.Id, d.Name, 0, d.Ingredients.Select(i => new ForecastIngredient(i.ProductId, i.Quantity)).ToList()))
            .ToList();

        var plan = EventPlanner.Plan(demand, model, menu, now, at, TimeSpan.FromMinutes(options.Value.StepMinutes));
        var suppliers = products.ToDictionary(p => p.Id, p => p.Supplier);

        return new EventPlanDto(
            now,
            at,
            plan.Feasible,
            request.Items.Sum(i => byId[i.DishId].Price * i.Portions),
            plan.RescuedCost,
            request.Items.Select(i => new EventDishDto(i.DishId, byId[i.DishId].Name, i.Portions, byId[i.DishId].Price)).ToList(),
            plan.Lines.Select(l => new EventLineDto(l.ProductId, l.Name, l.Unit, l.Need, l.AvailableAtEvent, l.Shortage, l.BuyQty,
                suppliers[l.ProductId], l.RescuedQty, l.RescuedCost)).ToList(),
            plan.StopsAfter.Select(s => new EventStopDto(s.DishId, s.Name, s.PortionsBefore)).ToList());
    }
}
