namespace Mise.Domain.Forecasting;

public enum ForecastConfidence
{
    /// <summary>Меньше суток истории или расхода не было.</summary>
    Low,
    /// <summary>От суток до трёх.</summary>
    Medium,
    /// <summary>Три дня и больше — есть профиль «час дня».</summary>
    High,
}

/// <summary>Расход продукта за час (агрегат consumption_hourly).</summary>
public sealed record HourlyConsumption(DateTimeOffset HourBucket, decimal Qty);

/// <param name="FlatAlpha">EWMA по часам для плоской скорости (≈ 2 / (24 + 1): память порядка суток).</param>
/// <param name="ProfileAlpha">EWMA по дням для одного и того же часа суток.</param>
/// <param name="ProfileMinDays">С какого объёма истории включается профиль «час дня».</param>
/// <param name="HistoryDays">Окно истории.</param>
/// <param name="MinPartialHours">Минимальный делитель для неполного часа (несколько минут не превращаются в огромную скорость).</param>
public sealed record ConsumptionRateOptions(
    decimal FlatAlpha = 0.08m,
    decimal ProfileAlpha = 0.5m,
    int ProfileMinDays = 3,
    int HistoryDays = 7,
    decimal MinPartialHours = 0.25m)
{
    public static ConsumptionRateOptions Default { get; } = new();

    public ConsumptionRateOptions Validate()
    {
        if (FlatAlpha is <= 0 or > 1 || ProfileAlpha is <= 0 or > 1)
            throw new ArgumentException("Коэффициенты EWMA — в (0;1].");
        if (ProfileMinDays < 1 || HistoryDays < ProfileMinDays || MinPartialHours is <= 0 or > 1)
            throw new ArgumentException("ProfileMinDays ≥ 1, HistoryDays ≥ ProfileMinDays, MinPartialHours в (0;1].");
        return this;
    }
}

/// <summary>
/// Оценка скорости расхода: плоская (в час) и, при достаточной истории, профиль по часам суток (UTC).
/// </summary>
/// <param name="HourlyProfile">24 значения «в час» по часу суток UTC; null — используется плоская скорость.</param>
/// <param name="HistoryHours">Длина истории, на которой построена оценка.</param>
public sealed record ConsumptionRate(decimal FlatPerHour, IReadOnlyList<decimal>? HourlyProfile, ForecastConfidence Confidence, decimal HistoryHours)
{
    public static ConsumptionRate None { get; } = new(0m, null, ForecastConfidence.Low, 0m);

    public decimal PerHourAt(DateTimeOffset at) => HourlyProfile is { } profile ? profile[at.UtcDateTime.Hour] : FlatPerHour;

    public decimal DailyRate => HourlyProfile?.Sum() ?? FlatPerHour * 24m;

    /// <summary>Ожидаемый расход на интервале — интеграл скорости (по часам суток, если есть профиль).</summary>
    public decimal Expected(DateTimeOffset from, DateTimeOffset to)
    {
        if (to <= from)
            return 0m;
        if (HourlyProfile is null)
            return FlatPerHour * Hours(to - from);

        var total = 0m;
        var cursor = from;
        while (cursor < to)
        {
            var hourEnd = ConsumptionRateEstimator.FloorToHour(cursor).AddHours(1);
            var end = hourEnd < to ? hourEnd : to;
            total += PerHourAt(cursor) * Hours(end - cursor);
            cursor = end;
        }
        return total;
    }

    internal static decimal Hours(TimeSpan span) => (decimal)span.Ticks / TimeSpan.TicksPerHour;
}

/// <summary>
/// EWMA-оценка расхода по часовым корзинам (PLAN §4.5): пустые часы — нули, а не «нет данных»; текущий неполный час
/// используется, только если полных часов ещё нет. При ≥ <see cref="ConsumptionRateOptions.ProfileMinDays"/> днях
/// истории — профиль «час дня» (EWMA одного и того же часа по дням).
/// </summary>
public static class ConsumptionRateEstimator
{
    private const int Precision = 4;

    /// <param name="history">Корзины продукта (любой порядок, могут быть не все часы).</param>
    /// <param name="historyStart">Начало истории расхода вообще (по всем продуктам); null — истории нет.</param>
    public static ConsumptionRate Estimate(IReadOnlyCollection<HourlyConsumption> history, DateTimeOffset? historyStart,
        DateTimeOffset now, ConsumptionRateOptions options)
    {
        if (historyStart is not { } start || start >= now)
            return ConsumptionRate.None;

        var windowStart = start > now.AddDays(-options.HistoryDays) ? start : now.AddDays(-options.HistoryDays);
        var historyHours = ConsumptionRate.Hours(now - windowStart);
        var byHour = history
            .Where(h => h.HourBucket >= FloorToHour(windowStart))
            .GroupBy(h => FloorToHour(h.HourBucket))
            .ToDictionary(g => g.Key, g => g.Sum(h => h.Qty));
        var currentHour = FloorToHour(now);

        var complete = new List<(DateTimeOffset Hour, decimal Qty)>();
        for (var hour = FloorToHour(windowStart); hour < currentHour; hour = hour.AddHours(1))
            complete.Add((hour, byHour.GetValueOrDefault(hour)));

        decimal flat;
        if (complete.Count == 0)
        {
            var elapsed = Math.Max(options.MinPartialHours, ConsumptionRate.Hours(now - (windowStart > currentHour ? windowStart : currentHour)));
            flat = byHour.GetValueOrDefault(currentHour) / elapsed;
        }
        else
        {
            flat = Ewma(complete.Select(c => c.Qty).ToList(), options.FlatAlpha, warmUp: 24);
        }
        flat = Round(Math.Max(0m, flat));

        IReadOnlyList<decimal>? profile = null;
        if (historyHours >= options.ProfileMinDays * 24m)
        {
            profile = Enumerable.Range(0, 24)
                .Select(h => complete.Where(c => c.Hour.UtcDateTime.Hour == h).Select(c => c.Qty).ToList())
                .Select(series => series.Count == 0 ? flat : Round(Math.Max(0m, Ewma(series, options.ProfileAlpha, warmUp: 1))))
                .ToList();
        }

        var total = byHour.Values.Sum();
        var confidence = total <= 0 || historyHours < 24m ? ForecastConfidence.Low
            : historyHours < 72m ? ForecastConfidence.Medium
            : ForecastConfidence.High;

        return new ConsumptionRate(flat, profile, confidence, decimal.Round(historyHours, 2));
    }

    public static DateTimeOffset FloorToHour(DateTimeOffset value)
    {
        var utc = value.ToUniversalTime();
        return new DateTimeOffset(utc.Year, utc.Month, utc.Day, utc.Hour, 0, 0, TimeSpan.Zero);
    }

    /// <summary>EWMA в хронологическом порядке; начальное значение — среднее первых <paramref name="warmUp"/> точек (меньше смещение).</summary>
    private static decimal Ewma(IReadOnlyList<decimal> series, decimal alpha, int warmUp)
    {
        var smoothed = series.Take(warmUp).Average();
        foreach (var value in series)
            smoothed = alpha * value + (1m - alpha) * smoothed;
        return smoothed;
    }

    private static decimal Round(decimal value) => decimal.Round(value, Precision, MidpointRounding.AwayFromZero);
}
