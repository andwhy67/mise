using FluentValidation.Results;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Mise.Application.Abstractions;
using Mise.Application.Common;
using Mise.Domain.Abstractions;
using Mise.Domain.Catalog;
using Mise.Domain.Common;
using Mise.Domain.Forecasting;
using Mise.Domain.Menu;
using Mise.Domain.Stock;

namespace Mise.Application.Forecasting;

/// <summary>Скорости расхода продуктов по consumption_hourly (EWMA, профиль «час дня») — общая основа прогнозов и hot offers.</summary>
public sealed class ConsumptionRates(IMiseDbContext db, IOptions<ForecastOptions> options)
{
    public async Task<IReadOnlyDictionary<Guid, ConsumptionRate>> LoadAsync(DateTimeOffset now, CancellationToken ct)
    {
        var rateOptions = options.Value.ToRateOptions();
        var since = now.AddDays(-rateOptions.HistoryDays);

        // Начало истории — первое списание под заказ в окне (по всем продуктам): продукт, который не заказывали, имеет скорость 0.
        var historyStart = await db.StockMovements.AsNoTracking()
            .Where(m => m.Type == MovementType.OrderConsumption && m.CreatedAt >= since && m.CreatedAt <= now)
            .MinAsync(m => (DateTimeOffset?)m.CreatedAt, ct);
        if (historyStart is null)
            return new Dictionary<Guid, ConsumptionRate>();

        var fromBucket = ConsumptionRateEstimator.FloorToHour(since);
        var rows = await db.ConsumptionHours.AsNoTracking()
            .Where(c => c.HourBucket >= fromBucket && c.HourBucket <= now)
            .Select(c => new { c.ProductId, c.HourBucket, c.QtyConsumed })
            .ToListAsync(ct);

        return rows
            .GroupBy(r => r.ProductId)
            .ToDictionary(
                g => g.Key,
                g => ConsumptionRateEstimator.Estimate(g.Select(r => new HourlyConsumption(r.HourBucket, r.QtyConsumed)).ToList(),
                    historyStart, now, rateOptions));
    }
}

/// <summary>Прогноз стоп-листа (§4.5) и предложения поставок (§4.6) на одной модели: расход по FEFO + истечение партий.</summary>
public sealed class ForecastService(
    IMiseDbContext db,
    IClock clock,
    IConsumptionAggregator aggregator,
    ConsumptionRates rates,
    IOptions<ForecastOptions> options)
{
    public async Task<StopListForecastDto> GetStopListForecastAsync(int? horizonHours, CancellationToken ct)
    {
        var settings = options.Value;
        var horizon = ValidateHorizon(horizonHours ?? settings.StopListHorizonHours, settings.MaxStopListHorizonHours);
        var model = await LoadAsync(horizon, ct);

        var dishes = model.Dishes.Select(d =>
        {
            var availability = MenuAvailability.Calculate(d, model.Usable);
            return new ForecastDish(d.Id, d.Name, availability.InStopList ? 0 : availability.MaxPortions,
                d.Ingredients.Select(i => new ForecastIngredient(i.ProductId, i.Quantity)).ToList());
        });

        var items = StopListForecaster.Forecast(dishes, model.Projections, model.Now)
            .Select(f =>
            {
                var dish = model.Dishes.Single(d => d.Id == f.DishId);
                var product = model.Products[f.LimitingProductId];
                var rate = model.RateOf(product.Id);
                var perPortion = dish.Ingredients.Single(i => i.ProductId == product.Id).Quantity;
                var productDto = new ForecastProductDto(product.Id, product.Name, product.Unit, model.Usable.GetValueOrDefault(product.Id),
                    decimal.Round(rate.PerHourAt(model.Now), 3));
                var hoursLeft = decimal.Round(Hours(f.Eta - model.Now), 2);
                return new StopListForecastItemDto(dish.Id, dish.Name, dish.Category, f.MaxPortionsNow, f.Eta, hoursLeft, productDto,
                    perPortion, f.Cause, rate.Confidence, Explain(f, product, productDto, perPortion, hoursLeft));
            })
            .ToList();

        return new StopListForecastDto(model.Now, horizon, items);
    }

    public async Task<SupplySuggestionsDto> GetSupplySuggestionsAsync(int? horizonHours, CancellationToken ct)
    {
        var settings = options.Value;
        var horizon = ValidateHorizon(horizonHours ?? settings.SupplyHorizonHours, settings.MaxSupplyHorizonHours);
        var model = await LoadAsync(horizon, ct);

        var items = model.Products.Values
            .Select(p => (Product: p, Suggestion: SupplySuggester.Suggest(
                new ForecastProduct(p.Id, p.Name, p.Unit, p.MinStockQty, p.PackSize, p.DefaultShelfLifeHours),
                model.Projections[p.Id], model.RateOf(p.Id), model.Now, TimeSpan.FromHours(horizon), settings.SafetyHours)))
            .Where(x => x.Suggestion is not null)
            .Select(x => ToDto(x.Product, x.Suggestion!, horizon, model.Now))
            .OrderByDescending(s => s.Urgency)
            .ThenBy(s => s.RunsOutAt ?? DateTimeOffset.MaxValue)
            .ThenBy(s => s.ProductName)
            .ToList();

        return new SupplySuggestionsDto(model.Now, horizon, items);
    }

    private async Task<ForecastModel> LoadAsync(int horizonHours, CancellationToken ct)
    {
        await aggregator.RefreshAsync(ct);
        var now = clock.UtcNow;
        var step = TimeSpan.FromMinutes(options.Value.StepMinutes);

        var products = await db.Products.AsNoTracking().ToDictionaryAsync(p => p.Id, ct);
        var batches = (await db.Batches.AsNoTracking()
                .Where(b => b.Status == BatchStatus.Active && b.RemainingQty > 0 && b.ExpiresAt > now)
                .Select(b => new { b.ProductId, Batch = new ForecastBatch(b.Id, b.ExpiresAt, b.RemainingQty) })
                .ToListAsync(ct))
            .ToLookup(b => b.ProductId, b => b.Batch);
        var dishes = await db.Dishes.AsNoTracking().Include(d => d.Ingredients).ToListAsync(ct);
        var productRates = await rates.LoadAsync(now, ct);

        var projections = products.Keys.ToDictionary(
            id => id,
            id => StockProjector.Project(batches[id].ToList(), productRates.GetValueOrDefault(id) ?? ConsumptionRate.None, now,
                TimeSpan.FromHours(horizonHours), step));
        var usable = projections.ToDictionary(p => p.Key, p => p.Value.InitialUsable);

        return new ForecastModel(now, products, dishes, usable, projections, productRates);
    }

    private static SupplySuggestionDto ToDto(Product product, SupplySuggestion s, int horizonHours, DateTimeOffset now)
    {
        var hoursToRunOut = s.RunsOutAt is { } runsOut ? decimal.Round(Hours(runsOut - now), 2) : (decimal?)null;
        var explanation = $"Заказать {RuFormat.Quantity(s.SuggestedQty, s.Unit)}: " + string.Join("; ", s.Reasons) + ".";
        return new SupplySuggestionDto(
            s.ProductId, s.ProductName, s.Unit, product.Category, s.Usable,
            decimal.Round(s.ExpectedDemand / horizonHours, 3), s.ExpectedDemand, s.Projected, s.ExpectedWaste, s.Safety, s.Need, s.Cap,
            s.CappedByShelfLife, s.PackSize, s.SuggestedQty, s.Urgency, s.RunsOutAt, hoursToRunOut, s.Confidence, s.Reasons, explanation);
    }

    private static string Explain(StopListForecastItem item, Product product, ForecastProductDto dto, decimal perPortion, decimal hoursLeft) =>
        item.Cause == ProjectionCause.Expiry
            ? $"Через {RuFormat.Hours(hoursLeft)} истечёт партия «{product.Name}», а оставшегося не хватит на порцию ({RuFormat.Quantity(perPortion, product.Unit)})."
            : $"«{product.Name}» расходуется ≈{RuFormat.Quantity(dto.RatePerHour, product.Unit)}/ч: остатка {RuFormat.Quantity(dto.UsableQty, product.Unit)} "
              + $"хватит на {RuFormat.Hours(hoursLeft)}, потом меньше порции ({RuFormat.Quantity(perPortion, product.Unit)}).";

    private static int ValidateHorizon(int hours, int max) =>
        hours is >= 1 && hours <= max
            ? hours
            : throw new[] { new ValidationFailure("horizonHours", $"Горизонт — от 1 до {max} ч.") }.ToValidationException();

    private static decimal Hours(TimeSpan span) => (decimal)span.Ticks / TimeSpan.TicksPerHour;

    private sealed record ForecastModel(
        DateTimeOffset Now,
        IReadOnlyDictionary<Guid, Product> Products,
        IReadOnlyList<Dish> Dishes,
        IReadOnlyDictionary<Guid, decimal> Usable,
        IReadOnlyDictionary<Guid, StockProjection> Projections,
        IReadOnlyDictionary<Guid, ConsumptionRate> Rates)
    {
        public ConsumptionRate RateOf(Guid productId) => Rates.GetValueOrDefault(productId) ?? ConsumptionRate.None;
    }
}
