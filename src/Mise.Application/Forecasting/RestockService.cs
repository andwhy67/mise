using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Mise.Application.Abstractions;
using Mise.Application.Menu;
using Mise.Domain.Abstractions;
using Mise.Domain.Common;
using Mise.Domain.Forecasting;
using Mise.Domain.Menu;
using Mise.Domain.Orders;

namespace Mise.Application.Forecasting;

public sealed record RestockLineDto(Guid ProductId, string ProductName, MeasureUnit Unit, decimal PerPortion, decimal Needed, decimal Usable, decimal Missing);

/// <param name="PortionsPerDay">Сколько порций продавали в сутки за последние Forecast:RestockHistoryDays дней.</param>
public sealed record RestockDishDto(
    Guid DishId,
    string DishName,
    string? Category,
    int TargetPortions,
    decimal PortionsPerDay,
    IReadOnlyList<RestockLineDto> Lines,
    string Explanation);

public sealed record RestockPurchaseDto(
    Guid ProductId,
    string ProductName,
    MeasureUnit Unit,
    string? Supplier,
    decimal PackSize,
    decimal Missing,
    decimal ToBuy,
    IReadOnlyList<string> Dishes,
    string Explanation);

public sealed record RestockDto(DateTimeOffset Now, int HorizonHours, int MinPortions, IReadOnlyList<RestockDishDto> Dishes, IReadOnlyList<RestockPurchaseDto> Purchases);

/// <summary>«Что докупить, чтобы вернуть блюда из стопа»: по блюдам в стопе из-за нехватки (выключенные вручную — не про закупку).</summary>
public sealed class RestockService(IMiseDbContext db, IClock clock, IOptions<ForecastOptions> options)
{
    public async Task<RestockDto> GetAsync(CancellationToken ct)
    {
        var settings = options.Value;
        var now = clock.UtcNow;
        var snapshot = await MenuSnapshot.LoadAsync(db, now, ct);
        var stopped = snapshot.Dishes
            .Where(d => MenuAvailability.Calculate(d, snapshot.Usable).Status == DishAvailabilityStatus.StopShortage)
            .ToList();
        if (stopped.Count == 0)
            return new RestockDto(now, settings.RestockHorizonHours, settings.RestockMinPortions, [], []);

        var since = now.AddDays(-settings.RestockHistoryDays);
        var ids = stopped.Select(d => d.Id).ToList();
        var sold = await db.Orders.AsNoTracking()
            .Where(o => o.CreatedAt >= since && o.CreatedAt <= now && o.Status != OrderStatus.Cancelled)
            .SelectMany(o => o.Items)
            .Where(i => ids.Contains(i.DishId))
            .GroupBy(i => i.DishId)
            .Select(g => new { DishId = g.Key, Portions = g.Sum(i => i.Quantity) })
            .ToDictionaryAsync(x => x.DishId, x => x.Portions, ct);

        var perDay = stopped.ToDictionary(d => d.Id, d => decimal.Round((decimal)sold.GetValueOrDefault(d.Id) / settings.RestockHistoryDays, 2));
        var plan = RestockPlanner.Plan(
            stopped.Select(d => new RestockDish(d.Id, d.Ingredients.Select(i => new ForecastIngredient(i.ProductId, i.Quantity)).ToList(), perDay[d.Id])).ToList(),
            snapshot.Products.Values.ToDictionary(p => p.Id, p => new RestockProduct(p.Id, snapshot.Usable.GetValueOrDefault(p.Id), p.PackSize)),
            TimeSpan.FromHours(settings.RestockHorizonHours),
            settings.RestockMinPortions);

        var dishById = stopped.ToDictionary(d => d.Id);
        string ProductName(Guid id) => snapshot.Products.GetValueOrDefault(id)?.Name ?? "?";
        MeasureUnit UnitOf(Guid id) => snapshot.Products.GetValueOrDefault(id)?.Unit ?? MeasureUnit.Gram;

        var dishes = plan.Dishes.Select(p =>
            {
                var dish = dishById[p.DishId];
                var lines = p.Lines.Select(l => new RestockLineDto(l.ProductId, ProductName(l.ProductId), UnitOf(l.ProductId), l.PerPortion, l.Needed, l.Usable, l.Missing)).ToList();
                var reason = perDay[p.DishId] * settings.RestockHorizonHours / 24m >= settings.RestockMinPortions
                    ? $"столько продают за {RuFormat.Hours(settings.RestockHorizonHours)}"
                    : "минимум, чтобы блюдо не ушло в стоп сразу";
                var missing = string.Join(", ", lines.Select(l => $"{l.ProductName} — {RuFormat.Quantity(l.Missing, l.Unit)}"));
                return new RestockDishDto(dish.Id, dish.Name, dish.Category, p.TargetPortions, perDay[p.DishId], lines,
                    $"Цель — {Portions(p.TargetPortions)} ({reason}); не хватает: {missing}.");
            })
            .OrderBy(d => d.DishName)
            .ToList();

        var purchases = plan.Purchases.Select(p =>
            {
                var product = snapshot.Products[p.ProductId];
                var names = p.Dishes.Select(id => dishById[id].Name).Order().ToList();
                var packNote = p.ToBuy > p.Missing ? $" (не хватает {RuFormat.Quantity(p.Missing, product.Unit)}, округлено до упаковки)" : "";
                return new RestockPurchaseDto(product.Id, product.Name, product.Unit, product.Supplier, product.PackSize, p.Missing, p.ToBuy, names,
                    $"Купить {RuFormat.Quantity(p.ToBuy, product.Unit)}{packNote} — вернёт в меню: {string.Join(", ", names)}.");
            })
            .OrderByDescending(p => p.Dishes.Count).ThenBy(p => p.ProductName)
            .ToList();

        return new RestockDto(now, settings.RestockHorizonHours, settings.RestockMinPortions, dishes, purchases);
    }

    /// <summary>1 порция, 2 порции, 5 порций, 21 порция.</summary>
    private static string Portions(int n) => n % 100 is >= 11 and <= 14 ? $"{n} порций" : (n % 10) switch
    {
        1 => $"{n} порция",
        2 or 3 or 4 => $"{n} порции",
        _ => $"{n} порций",
    };
}
