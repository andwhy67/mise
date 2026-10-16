using Mise.Domain.Common;

namespace Mise.Domain.Forecasting;

/// <param name="UnitCost">Цена за базовую единицу — для оценки «спасено» в рублях; null — цены нет.</param>
public sealed record EventProduct(
    Guid Id,
    string Name,
    MeasureUnit Unit,
    decimal PackSize,
    decimal? UnitCost,
    IReadOnlyList<ForecastBatch> Batches,
    ConsumptionRate Rate);

/// <param name="Need">Сколько продукта нужно на всё событие (общие ингредиенты блюд сложены).</param>
/// <param name="AvailableAtEvent">Сколько останется к началу события: обычный поток гостей съест своё, часть партий истечёт.</param>
/// <param name="BuyQty">Сколько докупить до события — нехватка, округлённая вверх до фасовки.</param>
/// <param name="RescuedQty">Сколько продукта событие заберёт из того, что без него истекло бы неиспользованным.</param>
public sealed record EventLine(
    Guid ProductId,
    string Name,
    MeasureUnit Unit,
    decimal Need,
    decimal AvailableAtEvent,
    decimal Shortage,
    decimal BuyQty,
    decimal RescuedQty,
    decimal? RescuedCost);

/// <summary>Блюдо, которое после события уйдёт в стоп: до него было <see cref="PortionsBefore"/> порций.</summary>
public sealed record EventDishImpact(Guid DishId, string Name, int PortionsBefore);

public sealed record EventPlan(DateTimeOffset At, IReadOnlyList<EventLine> Lines, IReadOnlyList<EventDishImpact> StopsAfter)
{
    public bool Feasible => Lines.All(l => l.Shortage == 0);
    public decimal RescuedCost => Lines.Sum(l => l.RescuedCost ?? 0m);
}

/// <summary>
/// «Что если»: банкет или большой заказ в момент <c>at</c>. Потребность — суммарная по блюдам (как у заказа);
/// остаток к событию — прогноз (обычный спрос по FEFO + истечение партий); событие тоже берёт по FEFO.
/// Сравнение прогноза с событием и без него даёт, что спасётся от порчи и какие блюда после него уйдут в стоп.
/// Ничего не резервирует и не списывает.
/// </summary>
public static class EventPlanner
{
    /// <summary>Сколько смотреть после события, чтобы увидеть, что без него истекло бы.</summary>
    public static readonly TimeSpan AfterHorizon = TimeSpan.FromHours(72);

    public static EventPlan Plan(
        IReadOnlyDictionary<Guid, decimal> demand,
        IReadOnlyDictionary<Guid, EventProduct> products,
        IReadOnlyList<ForecastDish> dishes,
        DateTimeOffset now,
        DateTimeOffset at,
        TimeSpan step)
    {
        if (at <= now)
            throw new ArgumentException("Событие должно быть в будущем.", nameof(at));
        var horizon = at - now + AfterHorizon;

        var baseline = products.Values.ToDictionary(p => p.Id,
            p => StockProjector.Project(p.Batches, p.Rate, now, horizon, step));
        var withEvent = demand.Where(d => products.ContainsKey(d.Key)).ToDictionary(d => d.Key,
            d => StockProjector.Project(products[d.Key].Batches, products[d.Key].Rate, now, horizon, step, (at, d.Value)));

        var lines = demand
            .Where(d => products.ContainsKey(d.Key))
            .Select(d =>
            {
                var product = products[d.Key];
                var available = baseline[d.Key].UsableAt(at);
                var shortage = Math.Max(0m, d.Value - withEvent[d.Key].EventServed);
                var pack = product.PackSize > 0 ? product.PackSize : 1m;
                var rescued = Math.Max(0m, baseline[d.Key].Wasted - withEvent[d.Key].Wasted);
                return new EventLine(product.Id, product.Name, product.Unit, d.Value, decimal.Round(available, 3), decimal.Round(shortage, 3),
                    shortage > 0 ? Math.Ceiling(shortage / pack) * pack : 0m,
                    decimal.Round(rescued, 3),
                    product.UnitCost is { } cost ? decimal.Round(rescued * cost, 2, MidpointRounding.AwayFromZero) : null);
            })
            .OrderByDescending(l => l.Shortage > 0).ThenByDescending(l => l.RescuedQty).ThenBy(l => l.Name)
            .ToList();

        int Portions(ForecastDish dish, Func<Guid, decimal> usable) =>
            dish.Ingredients.Count == 0
                ? 0
                : dish.Ingredients.Min(i => (int)Math.Floor(usable(i.ProductId) / i.QtyPerPortion));

        var stops = dishes
            .Where(d => d.Ingredients.Any(i => withEvent.ContainsKey(i.ProductId)) && d.Ingredients.All(i => baseline.ContainsKey(i.ProductId)))
            .Select(d => (Dish: d,
                Before: Portions(d, id => baseline[id].UsableAt(at)),
                After: Portions(d, id => withEvent.TryGetValue(id, out var p) ? p.UsableAt(at) : baseline[id].UsableAt(at))))
            .Where(x => x.Before > 0 && x.After == 0)
            .Select(x => new EventDishImpact(x.Dish.Id, x.Dish.Name, x.Before))
            .OrderBy(x => x.Name)
            .ToList();

        return new EventPlan(at, lines, stops);
    }
}
