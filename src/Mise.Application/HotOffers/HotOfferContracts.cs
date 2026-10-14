using Mise.Domain.Common;
using Mise.Domain.HotOffers;

namespace Mise.Application.HotOffers;

/// <summary>Настройки горячих предложений (секция "HotOffers"), формула — PLAN §4.4, docs/hot-offers.md.</summary>
public sealed class HotOfferOptions
{
    public const string SectionName = "HotOffers";

    /// <summary>Версия алгоритма по умолчанию: v1 (дословно §4.4) или v2 (+ «не успеет разойтись»).</summary>
    public string Scorer { get; set; } = HotOfferScorerV2.Name;

    /// <summary>Пересчёт по таймеру, секунды: время идёт — срочность растёт даже без событий.</summary>
    public int RefreshSeconds { get; set; } = 60;

    /// <summary>Окно истории расхода для baseline и скорости продаж, дни.</summary>
    public int HistoryDays { get; set; } = 7;

    public decimal MaxHorizonHours { get; set; } = HotOfferParameters.Default.MaxHorizonHours;
    public decimal HorizonShelfFraction { get; set; } = HotOfferParameters.Default.HorizonShelfFraction;
    public decimal VolumeBaseWeight { get; set; } = HotOfferParameters.Default.VolumeBaseWeight;
    public decimal ShareBaseWeight { get; set; } = HotOfferParameters.Default.ShareBaseWeight;
    public decimal SecondaryBonus { get; set; } = HotOfferParameters.Default.SecondaryBonus;
    public decimal SellThroughFloor { get; set; } = HotOfferParameters.Default.SellThroughFloor;
    public decimal FullCapacityPortions { get; set; } = HotOfferParameters.Default.FullCapacityPortions;
    public decimal EnterThreshold { get; set; } = HotOfferParameters.Default.EnterThreshold;
    public decimal ExitThreshold { get; set; } = HotOfferParameters.Default.ExitThreshold;
    public int TopN { get; set; } = HotOfferParameters.Default.TopN;

    public HotOfferParameters ToParameters() => new HotOfferParameters
    {
        MaxHorizonHours = MaxHorizonHours,
        HorizonShelfFraction = HorizonShelfFraction,
        VolumeBaseWeight = VolumeBaseWeight,
        ShareBaseWeight = ShareBaseWeight,
        SecondaryBonus = SecondaryBonus,
        SellThroughFloor = SellThroughFloor,
        FullCapacityPortions = FullCapacityPortions,
        EnterThreshold = EnterThreshold,
        ExitThreshold = ExitThreshold,
        TopN = TopN,
    }.Validate();

    public bool IsValid(IEnumerable<string> knownScorers)
    {
        try
        {
            ToParameters();
        }
        catch (ArgumentException)
        {
            return false;
        }
        return knownScorers.Contains(Scorer) && RefreshSeconds is >= 5 and <= 3600 && HistoryDays is >= 1 and <= 90;
    }
}

public sealed record HotOffersDto(DateTimeOffset GeneratedAt, string Scorer, IReadOnlyList<HotOfferDto> Offers);

/// <param name="Headline">«Горит: Шпинат — 4 ч, Лосось охл. — 12 ч».</param>
/// <param name="Explanation">Две главные причины человеческим языком.</param>
public sealed record HotOfferDto(
    int Rank,
    Guid DishId,
    string DishName,
    string? Category,
    decimal Price,
    int MaxPortions,
    decimal Score,
    string Headline,
    string Explanation,
    IReadOnlyList<HotOfferReasonDto> Reasons);

/// <param name="UnsoldQty">Сколько не успеет разойтись при обычном спросе (v2, если есть история расхода).</param>
/// <param name="Text">Причина человеческим языком.</param>
public sealed record HotOfferReasonDto(
    Guid ProductId,
    string ProductName,
    MeasureUnit Unit,
    decimal AtRiskQty,
    decimal? UnsoldQty,
    decimal HoursLeft,
    DateTimeOffset ExpiresAt,
    decimal QtyPerPortion,
    int PortionsBurned,
    decimal Pressure,
    decimal Share,
    decimal Contribution,
    string Text);
