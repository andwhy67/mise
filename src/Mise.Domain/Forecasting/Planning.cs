using Mise.Domain.Common;

namespace Mise.Domain.Forecasting;

public sealed record ForecastIngredient(Guid ProductId, decimal QtyPerPortion);

/// <param name="MaxPortions">Сколько порций можно сделать сейчас (0 — уже в стопе, прогноз не нужен).</param>
public sealed record ForecastDish(Guid Id, string Name, int MaxPortions, IReadOnlyList<ForecastIngredient> Ingredients);

/// <summary>Блюдо уйдёт в стоп: прогнозный остаток <see cref="LimitingProductId"/> станет меньше порции в момент <see cref="Eta"/>.</summary>
public sealed record StopListForecastItem(Guid DishId, string DishName, int MaxPortionsNow, DateTimeOffset Eta, Guid LimitingProductId, ProjectionCause Cause);

/// <summary>Прогноз стоп-листа (PLAN §4.5): для доступного блюда — минимум по ингредиентам момента «меньше порции».</summary>
public static class StopListForecaster
{
    public static IReadOnlyList<StopListForecastItem> Forecast(IEnumerable<ForecastDish> dishes,
        IReadOnlyDictionary<Guid, StockProjection> projections, DateTimeOffset now)
    {
        var items = new List<StopListForecastItem>();
        foreach (var dish in dishes.Where(d => d.MaxPortions >= 1))
        {
            var earliest = dish.Ingredients
                .Where(i => i.QtyPerPortion > 0 && projections.ContainsKey(i.ProductId))
                .Select(i => (i.ProductId, Crossing: projections[i.ProductId].FirstTimeBelow(i.QtyPerPortion)))
                .Where(x => x.Crossing is not null)
                .OrderBy(x => x.Crossing!.Value.At).ThenBy(x => x.ProductId)
                .FirstOrDefault();
            if (earliest.Crossing is { } crossing)
                items.Add(new StopListForecastItem(dish.Id, dish.Name, dish.MaxPortions, crossing.At, earliest.ProductId, crossing.Cause));
        }
        return items.OrderBy(i => i.Eta).ThenBy(i => i.DishId).ToList();
    }
}

public sealed record ForecastProduct(Guid Id, string Name, MeasureUnit Unit, decimal MinStockQty, decimal PackSize, int? DefaultShelfLifeHours);

public enum SuggestionUrgency
{
    Normal,
    /// <summary>Кончится в течение суток или уже ниже минимума.</summary>
    High,
    /// <summary>Кончится в течение 12 ч или пригодного остатка нет.</summary>
    Critical,
}

/// <param name="Projected">Остаток, который доживёт (не истечёт неиспользованным) на горизонте.</param>
/// <param name="ExpectedWaste">Сколько текущего остатка истечёт неиспользованным.</param>
/// <param name="Cap">Больше не успеем использовать до порчи: скорость × срок годности (не ниже минимума).</param>
public sealed record SupplySuggestion(
    Guid ProductId,
    string ProductName,
    MeasureUnit Unit,
    decimal Usable,
    decimal ExpectedDemand,
    decimal Projected,
    decimal ExpectedWaste,
    decimal Safety,
    decimal Need,
    decimal Cap,
    bool CappedByShelfLife,
    decimal PackSize,
    decimal SuggestedQty,
    SuggestionUrgency Urgency,
    DateTimeOffset? RunsOutAt,
    ForecastConfidence Confidence,
    IReadOnlyList<string> Reasons);

/// <summary>
/// Предложение поставки (PLAN §4.6) на горизонте Hz:
/// demand = расход за Hz; projected = остаток, который доживёт; safety = max(min_stock, скорость × 12 ч);
/// need = max(0, demand + safety − projected); cap = max(скорость × срок годности, min_stock);
/// suggest = ⌈min(need, cap) / pack⌉ × pack.
/// </summary>
public static class SupplySuggester
{
    public const decimal DefaultSafetyHours = 12m;
    private static readonly TimeSpan CriticalWithin = TimeSpan.FromHours(12);
    private static readonly TimeSpan HighWithin = TimeSpan.FromHours(24);

    public static SupplySuggestion? Suggest(ForecastProduct product, StockProjection projection, ConsumptionRate rate,
        DateTimeOffset now, TimeSpan horizon, decimal safetyHours)
    {
        var horizonHours = ConsumptionRate.Hours(horizon);
        var demand = Round(rate.Expected(now, now + horizon));
        var usable = projection.InitialUsable;
        var projected = usable - projection.Wasted;
        var perHour = demand / horizonHours;
        var safety = Round(Math.Max(product.MinStockQty, perHour * safetyHours));
        var need = Round(Math.Max(0m, demand + safety - projected));
        if (need <= 0)
            return null;

        // Без нормы срока ограничение по порче считается на горизонте планирования.
        var shelfLife = product.DefaultShelfLifeHours is { } hours ? hours : horizonHours;
        var cap = Round(Math.Max(perHour * shelfLife, product.MinStockQty));
        var cappedByShelfLife = cap < need;
        var pack = product.PackSize > 0 ? product.PackSize : 1m;
        var suggested = Math.Ceiling(Math.Min(need, cap) / pack) * pack;
        if (suggested <= 0)
            return null;

        var urgency = usable <= 0 || projection.RunsOutAt - now <= CriticalWithin ? SuggestionUrgency.Critical
            : usable < product.MinStockQty || projection.RunsOutAt - now <= HighWithin ? SuggestionUrgency.High
            : SuggestionUrgency.Normal;

        return new SupplySuggestion(
            product.Id, product.Name, product.Unit, usable, demand, projected, projection.Wasted, safety, need, cap,
            cappedByShelfLife, pack, suggested, urgency, projection.RunsOutAt, rate.Confidence,
            Reasons(product, projection, rate, now, horizonHours, demand, usable, cappedByShelfLife));
    }

    private static List<string> Reasons(ForecastProduct product, StockProjection projection, ConsumptionRate rate, DateTimeOffset now,
        decimal horizonHours, decimal demand, decimal usable, bool cappedByShelfLife)
    {
        var reasons = new List<string>();
        if (projection.RunsOutAt is { } runsOut)
            reasons.Add(runsOut <= now ? "спрос уже не покрывается остатком" : $"исчерпается через {RuFormat.Hours(ConsumptionRate.Hours(runsOut - now))}");
        if (usable <= 0)
            reasons.Add("пригодного остатка нет");
        else if (usable < product.MinStockQty)
            reasons.Add($"ниже минимального остатка ({RuFormat.Quantity(usable, product.Unit)} при минимуме {RuFormat.Quantity(product.MinStockQty, product.Unit)})");
        if (projection.Wasted > 0)
            reasons.Add($"{RuFormat.Quantity(projection.Wasted, product.Unit)} истечёт неиспользованным");
        if (cappedByShelfLife)
            reasons.Add("объём ограничен сроком годности: больше не успеем использовать до порчи");
        if (demand <= 0 && rate.Confidence == ForecastConfidence.Low)
            reasons.Add("нет истории расхода — довозим до минимального остатка");
        if (reasons.Count == 0)
            reasons.Add($"расход {RuFormat.Quantity(demand, product.Unit)} за {RuFormat.Hours(horizonHours)} с запасом не покрывается остатком");
        return reasons;
    }

    private static decimal Round(decimal value) => decimal.Round(value, 3, MidpointRounding.AwayFromZero);
}
