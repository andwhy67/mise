namespace Mise.Domain.HotOffers;

/// <summary>
/// Общая часть §4.4: давление продуктов → вклад в блюда с учётом доли → ёмкость → итог с бонусом за несколько ингредиентов.
/// Версии отличаются только оценкой риска продукта (<see cref="AssessProduct"/>).
/// </summary>
public abstract class HotOfferScorerBase : IHotOfferScorer
{
    private const int Precision = 4;

    public abstract string Version { get; }

    public IReadOnlyList<HotOffer> Score(HotOfferInput input, HotOfferParameters parameters)
    {
        var batchesByProduct = input.Batches
            .Where(b => b.RemainingQty > 0 && b.ExpiresAt > input.Now)
            .OrderBy(b => b.ExpiresAt).ThenBy(b => b.BatchId)
            .ToLookup(b => b.ProductId);

        var risks = input.Products
            .Select(p => AssessProduct(p, batchesByProduct[p.Id].ToList(), input.Now, parameters))
            .Where(r => r is { Pressure: > 0 })
            .ToDictionary(r => r!.Product.Id, r => r!);

        var available = input.Dishes.Where(d => d.MaxPortions >= 1).ToList();
        // Учёт «ингредиент в нескольких блюдах»: доля блюда — среди доступных блюд с этим продуктом.
        var totalQtyByProduct = available
            .SelectMany(d => d.Ingredients)
            .GroupBy(i => i.ProductId)
            .ToDictionary(g => g.Key, g => g.Sum(i => i.QtyPerPortion));

        var offers = new List<HotOffer>();
        foreach (var dish in available)
        {
            var reasons = dish.Ingredients
                .Where(i => i.QtyPerPortion > 0 && risks.ContainsKey(i.ProductId))
                .Select(i => ToReason(dish, i, risks[i.ProductId], totalQtyByProduct[i.ProductId], parameters))
                .OrderByDescending(r => r.Contribution).ThenBy(r => r.ProductId)
                .ToList();
            if (reasons.Count == 0)
                continue;

            var capacity = Math.Min(1m, dish.MaxPortions / parameters.FullCapacityPortions);
            var main = reasons[0].Contribution;
            var rest = reasons.Skip(1).Sum(r => r.Contribution);
            var score = decimal.Round(100m * capacity * Math.Min(1m, main + parameters.SecondaryBonus * rest), 1, MidpointRounding.AwayFromZero);
            if (score > 0)
                offers.Add(new HotOffer(dish.Id, dish.Name, dish.MaxPortions, score, decimal.Round(capacity, Precision), reasons));
        }

        return offers.OrderByDescending(o => o.Score).ThenBy(o => o.DishId).ToList();
    }

    /// <summary>Риск продукта: срочность T, объём под риском и итоговое давление P; null — продукт не горит.</summary>
    protected abstract ProductRisk? AssessProduct(HotOfferProduct product, IReadOnlyList<HotOfferBatch> fefoBatches,
        DateTimeOffset now, HotOfferParameters parameters);

    /// <summary>Горизонт H = min(72 ч, 0,5 × срок): по норме продукта, а без неё — по полному сроку партии.</summary>
    protected static decimal HorizonHours(HotOfferProduct product, HotOfferBatch batch, HotOfferParameters parameters)
    {
        var shelfLife = product.DefaultShelfLifeHours is { } hours ? hours : Hours(batch.ExpiresAt - batch.ReceivedAt);
        return Math.Min(parameters.MaxHorizonHours, parameters.HorizonShelfFraction * shelfLife);
    }

    /// <summary>Срочность партии u = (1 − hoursLeft / H)², 0 вне горизонта.</summary>
    protected static decimal Urgency(decimal hoursLeft, decimal horizon)
    {
        if (horizon <= 0 || hoursLeft >= horizon)
            return 0m;
        var x = 1m - hoursLeft / horizon;
        return x * x;
    }

    /// <summary>Давление P = T × (w + (1 − w) × V), V = min(1, объём / baseline); baseline = 0 → V = 1 (assumptions A5).</summary>
    protected static decimal Pressure(decimal urgency, decimal volume, decimal baseline, HotOfferParameters parameters)
    {
        var v = baseline <= 0 ? 1m : Math.Min(1m, volume / baseline);
        return urgency * (parameters.VolumeBaseWeight + (1m - parameters.VolumeBaseWeight) * v);
    }

    protected static decimal Hours(TimeSpan span) => (decimal)span.Ticks / TimeSpan.TicksPerHour;

    private static HotOfferReason ToReason(HotOfferDish dish, HotOfferIngredient ingredient, ProductRisk risk, decimal totalQty,
        HotOfferParameters parameters)
    {
        var share = ingredient.QtyPerPortion / totalQty;
        var contribution = risk.Pressure * (parameters.ShareBaseWeight + (1m - parameters.ShareBaseWeight) * share);
        var portions = (int)Math.Min(dish.MaxPortions, Math.Floor(risk.AtRiskQty / ingredient.QtyPerPortion));
        return new HotOfferReason(
            risk.Product.Id, risk.Product.Name, risk.Product.Unit,
            risk.AtRiskQty, risk.UnsoldQty,
            decimal.Round(risk.HoursLeft, 2, MidpointRounding.AwayFromZero), risk.ExpiresAt,
            ingredient.QtyPerPortion, portions,
            decimal.Round(risk.Pressure, Precision, MidpointRounding.AwayFromZero),
            decimal.Round(share, Precision, MidpointRounding.AwayFromZero),
            decimal.Round(contribution, Precision, MidpointRounding.AwayFromZero));
    }

    protected sealed record ProductRisk(
        HotOfferProduct Product,
        decimal Pressure,
        decimal AtRiskQty,
        decimal? UnsoldQty,
        decimal HoursLeft,
        DateTimeOffset ExpiresAt);
}

/// <summary>
/// v1 — дословно PLAN §4.4: T = max u по партиям в горизонте, V = min(1, Σ остатка в горизонте / baseline).
/// </summary>
public sealed class HotOfferScorerV1 : HotOfferScorerBase
{
    public const string Name = "v1";

    public override string Version => Name;

    protected override ProductRisk? AssessProduct(HotOfferProduct product, IReadOnlyList<HotOfferBatch> fefoBatches,
        DateTimeOffset now, HotOfferParameters parameters)
    {
        var risky = fefoBatches
            .Select(b => (Batch: b, HoursLeft: Hours(b.ExpiresAt - now), Horizon: HorizonHours(product, b, parameters)))
            .Where(x => x.HoursLeft < x.Horizon)
            .ToList();
        if (risky.Count == 0)
            return null;

        var urgency = risky.Max(x => Urgency(x.HoursLeft, x.Horizon));
        var atRisk = risky.Sum(x => x.Batch.RemainingQty);
        var earliest = risky[0];
        return new ProductRisk(product, Pressure(urgency, atRisk, product.Baseline, parameters), atRisk, null,
            earliest.HoursLeft, earliest.Batch.ExpiresAt);
    }
}

/// <summary>
/// v2 — v1 + «не успеет разойтись» (§4.4: опаснее партия, которая не разойдётся): из объёма под риском вычитается то,
/// что обычный спрос (скорость по истории) съест по FEFO до истечения каждой партии; срочность берётся по партиям,
/// у которых останется непроданный остаток, а давление умножается на f + (1 − f) × (не разойдётся / под риском).
/// Без истории расхода совпадает с v1.
/// </summary>
public sealed class HotOfferScorerV2 : HotOfferScorerBase
{
    public const string Name = "v2";

    public override string Version => Name;

    protected override ProductRisk? AssessProduct(HotOfferProduct product, IReadOnlyList<HotOfferBatch> fefoBatches,
        DateTimeOffset now, HotOfferParameters parameters)
    {
        var hourlyRate = product.DailyConsumption / 24m;
        var soldSoFar = 0m;
        var batches = new List<(HotOfferBatch Batch, decimal HoursLeft, decimal Horizon, decimal Unsold)>();
        foreach (var batch in fefoBatches)
        {
            var hoursLeft = Hours(batch.ExpiresAt - now);
            // Спрос до истечения этой партии сначала забирает более ранние (FEFO), остальное — из неё.
            var canSell = Math.Max(0m, hourlyRate * hoursLeft - soldSoFar);
            var sold = Math.Min(batch.RemainingQty, canSell);
            soldSoFar += sold;
            batches.Add((batch, hoursLeft, HorizonHours(product, batch, parameters), batch.RemainingQty - sold));
        }

        var risky = batches.Where(x => x.HoursLeft < x.Horizon).ToList();
        if (risky.Count == 0)
            return null;

        var atRisk = risky.Sum(x => x.Batch.RemainingQty);
        var unsold = decimal.Round(risky.Sum(x => x.Unsold), 3, MidpointRounding.AwayFromZero);
        var unsoldBatches = risky.Where(x => x.Unsold > 0).ToList();
        var driving = unsoldBatches.Count > 0 ? unsoldBatches : risky;
        var urgency = driving.Max(x => Urgency(x.HoursLeft, x.Horizon));
        var earliest = driving[0];

        var wasteShare = atRisk > 0 ? unsold / atRisk : 1m;
        var sellThrough = parameters.SellThroughFloor + (1m - parameters.SellThroughFloor) * wasteShare;
        return new ProductRisk(product, Pressure(urgency, unsold, product.Baseline, parameters) * sellThrough, atRisk,
            hourlyRate > 0 ? unsold : null, earliest.HoursLeft, earliest.Batch.ExpiresAt);
    }
}
