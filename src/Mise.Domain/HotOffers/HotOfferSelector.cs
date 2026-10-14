using Mise.Domain.Common;

namespace Mise.Domain.HotOffers;

/// <summary>
/// Выдача топ-N с гистерезисом (PLAN §15, «скачущие» предложения): новое блюдо входит при score ≥ EnterThreshold,
/// уже показанное держится, пока score ≥ ExitThreshold. Сортировка устойчивая — по score, затем по id блюда.
/// </summary>
public static class HotOfferSelector
{
    public static IReadOnlyList<HotOffer> Select(IEnumerable<HotOffer> scored, IReadOnlyCollection<Guid> previous, HotOfferParameters parameters)
    {
        var shown = previous as IReadOnlySet<Guid> ?? previous.ToHashSet();
        return scored
            .Where(o => o.Score >= parameters.EnterThreshold || (shown.Contains(o.DishId) && o.Score >= parameters.ExitThreshold))
            .OrderByDescending(o => o.Score).ThenBy(o => o.DishId)
            .Take(parameters.TopN)
            .ToList();
    }
}

/// <param name="Headline">Короткая строка для карточки: что горит и когда.</param>
/// <param name="Explanation">Две главные причины человеческим языком.</param>
/// <param name="Reasons">Текст для каждой причины (в порядке вклада).</param>
public sealed record HotOfferText(string Headline, string Explanation, IReadOnlyList<string> Reasons);

/// <summary>Объяснение рекомендации (CLAUDE.md, правило 9): структурные причины → понятный текст.</summary>
public static class HotOfferExplainer
{
    public const int ReasonsInExplanation = 2;

    public static HotOfferText Explain(HotOffer offer)
    {
        var reasons = offer.Reasons.Select(Describe).ToList();
        var headline = "Горит: " + string.Join(", ",
            offer.Reasons.Take(ReasonsInExplanation).Select(r => $"{r.ProductName} — {RuFormat.Hours(r.HoursLeft)}"));
        return new HotOfferText(headline, string.Join(" ", reasons.Take(ReasonsInExplanation)), reasons);
    }

    /// <summary>«Сливки 33%: 2 л истекает через 6 ч; в порции 150 мл → блюдо поможет списать до 13 порций сырья.»</summary>
    public static string Describe(HotOfferReason reason)
    {
        var unsold = reason.UnsoldQty switch
        {
            null => string.Empty,
            0m => ", но обычный спрос, скорее всего, успеет его разобрать",
            var qty when qty < reason.AtRiskQty => $", из них не успеет разойтись ≈{RuFormat.Quantity(qty.Value, reason.Unit)}",
            _ => string.Empty,
        };
        var portions = reason.PortionsBurned > 0
            ? $" → блюдо поможет списать до {RuFormat.PortionsGenitive(reason.PortionsBurned)} сырья"
            : string.Empty;
        return $"{reason.ProductName}: {RuFormat.Quantity(reason.AtRiskQty, reason.Unit)} истекает через {RuFormat.Hours(reason.HoursLeft)}{unsold}; "
            + $"в порции {RuFormat.Quantity(reason.QtyPerPortion, reason.Unit)}{portions}.";
    }
}
