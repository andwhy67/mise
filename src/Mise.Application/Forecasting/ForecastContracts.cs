using Mise.Domain.Common;
using Mise.Domain.Forecasting;

namespace Mise.Application.Forecasting;

/// <summary>Настройки расхода и прогнозов (секция "Forecast", PLAN §4.5–4.6).</summary>
public sealed class ForecastOptions
{
    public const string SectionName = "Forecast";

    public int HistoryDays { get; set; } = ConsumptionRateOptions.Default.HistoryDays;
    public decimal FlatAlpha { get; set; } = ConsumptionRateOptions.Default.FlatAlpha;
    public decimal ProfileAlpha { get; set; } = ConsumptionRateOptions.Default.ProfileAlpha;
    public int ProfileMinDays { get; set; } = ConsumptionRateOptions.Default.ProfileMinDays;

    /// <summary>Шаг симуляции остатка, минуты.</summary>
    public int StepMinutes { get; set; } = 15;

    /// <summary>Горизонт прогноза стоп-листа по умолчанию и максимум, часы.</summary>
    public int StopListHorizonHours { get; set; } = 24;
    public int MaxStopListHorizonHours { get; set; } = 72;

    /// <summary>Горизонт планирования поставок Hz по умолчанию и максимум, часы.</summary>
    public int SupplyHorizonHours { get; set; } = 72;
    public int MaxSupplyHorizonHours { get; set; } = 336;

    /// <summary>Страховой запас: скорость × столько часов (но не меньше минимального остатка).</summary>
    public decimal SafetyHours { get; set; } = SupplySuggester.DefaultSafetyHours;

    /// <summary>«Что докупить для блюд из стопа»: горизонт спроса, минимум порций и за сколько дней брать продажи.</summary>
    public int RestockHorizonHours { get; set; } = 24;
    public int RestockMinPortions { get; set; } = 5;
    public int RestockHistoryDays { get; set; } = 7;

    /// <summary>Период фонового пересчёта consumption_hourly, секунды.</summary>
    public int AggregationIntervalSeconds { get; set; } = 60;

    public ConsumptionRateOptions ToRateOptions() =>
        new ConsumptionRateOptions(FlatAlpha, ProfileAlpha, ProfileMinDays, HistoryDays).Validate();

    public bool IsValid()
    {
        try
        {
            ToRateOptions();
        }
        catch (ArgumentException)
        {
            return false;
        }
        return StepMinutes is >= 1 and <= 60
            && StopListHorizonHours >= 1 && MaxStopListHorizonHours >= StopListHorizonHours && MaxStopListHorizonHours <= 168
            && SupplyHorizonHours >= 1 && MaxSupplyHorizonHours >= SupplyHorizonHours && MaxSupplyHorizonHours <= 720
            && SafetyHours is >= 0 and <= 168
            && RestockHorizonHours is >= 1 and <= 168 && RestockMinPortions is >= 1 and <= 100 && RestockHistoryDays is >= 1 and <= 90
            && AggregationIntervalSeconds is >= 5 and <= 3600;
    }
}

public sealed record ForecastProductDto(Guid ProductId, string ProductName, MeasureUnit Unit, decimal UsableQty, decimal RatePerHour);

/// <param name="Cause">Consumption — остаток съедят заказы; Expiry — партия истечёт, и остального не хватит на порцию.</param>
/// <param name="Confidence">По истории расхода ограничивающего продукта.</param>
public sealed record StopListForecastItemDto(
    Guid DishId,
    string DishName,
    string? Category,
    int MaxPortionsNow,
    DateTimeOffset Eta,
    decimal HoursLeft,
    ForecastProductDto LimitingProduct,
    decimal QtyPerPortion,
    ProjectionCause Cause,
    ForecastConfidence Confidence,
    string Explanation);

public sealed record StopListForecastDto(DateTimeOffset GeneratedAt, int HorizonHours, IReadOnlyList<StopListForecastItemDto> Items);

/// <param name="RatePerHour">Средняя ожидаемая скорость расхода на горизонте.</param>
/// <param name="HoursToRunOut">Через сколько часов спрос перестанет покрываться (null — хватит).</param>
public sealed record SupplySuggestionDto(
    Guid ProductId,
    string ProductName,
    MeasureUnit Unit,
    string? Category,
    decimal UsableQty,
    decimal RatePerHour,
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
    decimal? HoursToRunOut,
    ForecastConfidence Confidence,
    IReadOnlyList<string> Reasons,
    string Explanation);

public sealed record SupplySuggestionsDto(DateTimeOffset GeneratedAt, int HorizonHours, IReadOnlyList<SupplySuggestionDto> Items);
