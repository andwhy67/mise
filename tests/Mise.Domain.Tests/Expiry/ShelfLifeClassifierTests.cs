using Mise.Domain.Expiry;

namespace Mise.Domain.Tests.Expiry;

public class ShelfLifeClassifierTests
{
    private static readonly DateTimeOffset Received = new(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly ShelfLifeThresholds Defaults = ShelfLifeThresholds.Default; // warn: ≤50% и ≤24ч; critical: ≤20% и ≤6ч

    private static ShelfLifeInfo Classify(double shelfLifeHours, double hoursPassed) =>
        ShelfLifeClassifier.Classify(Received, Received.AddHours(shelfLifeHours), Received.AddHours(hoursPassed), Defaults);

    [Fact]
    public void Fresh_batch_reports_hours_left_and_fraction()
    {
        var info = Classify(shelfLifeHours: 100, hoursPassed: 10);

        Assert.Equal(ShelfLifeStatus.Fresh, info.Status);
        Assert.Equal(90m, info.HoursLeft);
        Assert.Equal(0.9m, info.RemainingFraction);
    }

    [Theory]
    // Длинный срок (30 дней): статус определяется абсолютными часами.
    [InlineData(720, 600, ShelfLifeStatus.Fresh)]      // 120 ч осталось (17%), но > 24 ч
    [InlineData(720, 697, ShelfLifeStatus.Warning)]    // 23 ч
    [InlineData(720, 715, ShelfLifeStatus.Critical)]   // 5 ч
    // Короткий срок (12 ч): статус определяется долей срока.
    [InlineData(12, 5, ShelfLifeStatus.Fresh)]         // 7 ч = 58%
    [InlineData(12, 6, ShelfLifeStatus.Warning)]       // 6 ч = 50% (граница включительно)
    [InlineData(12, 9.6, ShelfLifeStatus.Critical)]    // 2.4 ч = 20%
    public void Thresholds_require_both_fraction_and_hours(double shelfLife, double passed, ShelfLifeStatus expected)
    {
        Assert.Equal(expected, Classify(shelfLife, passed).Status);
    }

    [Theory]
    [InlineData(48, 48)]
    [InlineData(48, 60)]
    public void Batch_is_expired_from_the_exact_expiry_moment(double shelfLife, double passed)
    {
        var info = Classify(shelfLife, passed);

        Assert.Equal(ShelfLifeStatus.Expired, info.Status);
        Assert.Equal(0m, info.HoursLeft);
        Assert.Equal(0m, info.RemainingFraction);
    }

    [Fact]
    public void Custom_thresholds_are_respected()
    {
        var strict = new ShelfLifeThresholds(WarningFraction: 0.8m, WarningHours: 1000m, CriticalFraction: 0.5m, CriticalHours: 1000m);

        var info = ShelfLifeClassifier.Classify(Received, Received.AddHours(10), Received.AddHours(3), strict);

        Assert.Equal(ShelfLifeStatus.Warning, info.Status);
    }

    [Fact]
    public void Invalid_thresholds_are_rejected()
    {
        Assert.Throws<ArgumentException>(() => new ShelfLifeThresholds(0.1m, 24m, 0.2m, 6m).Validate());
    }
}
