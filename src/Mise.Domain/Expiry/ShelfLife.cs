namespace Mise.Domain.Expiry;

public enum ShelfLifeStatus
{
    Fresh,
    Warning,
    Critical,
    Expired,
}

/// <summary>
/// Пороги статуса срока. Статус наступает, когда выполнены ОБА условия — по доле оставшегося срока и по часам:
/// у долгих продуктов срабатывают часы, у коротких — доля (docs/assumptions.md, A6).
/// </summary>
public sealed record ShelfLifeThresholds(
    decimal WarningFraction,
    decimal WarningHours,
    decimal CriticalFraction,
    decimal CriticalHours)
{
    public static ShelfLifeThresholds Default { get; } = new(0.5m, 24m, 0.2m, 6m);

    public ShelfLifeThresholds Validate()
    {
        if (CriticalFraction is <= 0 or > 1 || WarningFraction is <= 0 or > 1 || CriticalFraction > WarningFraction)
            throw new ArgumentException("Доли порогов должны быть в (0;1] и critical ≤ warning.");
        if (CriticalHours <= 0 || WarningHours <= 0 || CriticalHours > WarningHours)
            throw new ArgumentException("Часы порогов должны быть > 0 и critical ≤ warning.");
        return this;
    }
}

public readonly record struct ShelfLifeInfo(ShelfLifeStatus Status, decimal HoursLeft, decimal RemainingFraction);

public static class ShelfLifeClassifier
{
    private const int Precision = 4;

    public static ShelfLifeInfo Classify(
        DateTimeOffset receivedAt,
        DateTimeOffset expiresAt,
        DateTimeOffset now,
        ShelfLifeThresholds thresholds)
    {
        if (now >= expiresAt)
            return new ShelfLifeInfo(ShelfLifeStatus.Expired, 0m, 0m);

        var left = expiresAt - now;
        var total = expiresAt - receivedAt;
        var hoursLeft = decimal.Round((decimal)left.Ticks / TimeSpan.TicksPerHour, Precision);
        var fraction = total.Ticks <= 0
            ? 0m
            : Math.Min(1m, decimal.Round((decimal)left.Ticks / total.Ticks, Precision));

        var status =
            fraction <= thresholds.CriticalFraction && hoursLeft <= thresholds.CriticalHours ? ShelfLifeStatus.Critical
            : fraction <= thresholds.WarningFraction && hoursLeft <= thresholds.WarningHours ? ShelfLifeStatus.Warning
            : ShelfLifeStatus.Fresh;

        return new ShelfLifeInfo(status, hoursLeft, fraction);
    }
}
