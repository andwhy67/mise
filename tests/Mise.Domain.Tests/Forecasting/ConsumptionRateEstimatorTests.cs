using Mise.Domain.Forecasting;

namespace Mise.Domain.Tests.Forecasting;

public class ConsumptionRateEstimatorTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 12, 12, 30, 0, TimeSpan.Zero);
    private static readonly ConsumptionRateOptions Options = ConsumptionRateOptions.Default;

    [Fact]
    public void No_history_means_zero_rate_with_low_confidence()
    {
        var rate = ConsumptionRateEstimator.Estimate([], historyStart: null, Now, Options);

        Assert.Equal(0m, rate.FlatPerHour);
        Assert.Null(rate.HourlyProfile);
        Assert.Equal(ForecastConfidence.Low, rate.Confidence);
        Assert.Equal(0m, rate.Expected(Now, Now.AddHours(10)));
    }

    [Fact]
    public void Steady_consumption_gives_its_rate_and_empty_hours_count_as_zero()
    {
        var start = Now.AddHours(-48);
        var steady = Hours(start, 48, _ => 10m);
        var sparse = Hours(start, 48, h => h % 2 == 0 ? 20m : 0m).Where(x => x.Qty > 0).ToList();

        var steadyRate = ConsumptionRateEstimator.Estimate(steady, start, Now, Options);
        var sparseRate = ConsumptionRateEstimator.Estimate(sparse, start, Now, Options);

        Assert.InRange(steadyRate.FlatPerHour, 9.99m, 10.01m);
        Assert.InRange(sparseRate.FlatPerHour, 8m, 12m); // пропуски — нули, а не «нет данных»
        Assert.Equal(ForecastConfidence.Medium, steadyRate.Confidence);
        Assert.Null(steadyRate.HourlyProfile); // меньше 3 дней — плоская скорость
        Assert.Equal(240m, decimal.Round(steadyRate.DailyRate));
    }

    [Fact]
    public void Ewma_follows_a_recent_change_faster_than_a_plain_mean()
    {
        var start = Now.AddHours(-48);
        var history = Hours(start, 48, h => h < 36 ? 10m : 30m);

        var rate = ConsumptionRateEstimator.Estimate(history, start, Now, Options);

        Assert.True(rate.FlatPerHour > 15m, $"EWMA {rate.FlatPerHour} должна быть ближе к новому уровню, чем среднее 15");
        Assert.True(rate.FlatPerHour < 30m);
    }

    [Fact]
    public void Three_days_of_history_enable_hour_of_day_profile_with_high_confidence()
    {
        var start = new DateTimeOffset(2026, 10, 8, 0, 0, 0, TimeSpan.Zero);
        // Обед 10:00–12:00 UTC — по 60, в остальное время — по 5.
        var history = Hours(start, 24 * 4 + 12, h => (h % 24) is 10 or 11 ? 60m : 5m);

        var rate = ConsumptionRateEstimator.Estimate(history, start, Now, Options);

        Assert.Equal(ForecastConfidence.High, rate.Confidence);
        Assert.NotNull(rate.HourlyProfile);
        Assert.InRange(rate.PerHourAt(new DateTimeOffset(2026, 10, 12, 10, 15, 0, TimeSpan.Zero)), 59m, 61m);
        Assert.InRange(rate.PerHourAt(new DateTimeOffset(2026, 10, 12, 3, 0, 0, TimeSpan.Zero)), 4.9m, 5.1m);
        // Ожидание на сутки — интеграл профиля: 2 × 60 + 22 × 5 = 230.
        Assert.InRange(rate.Expected(Now, Now.AddDays(1)), 228m, 232m);
    }

    [Fact]
    public void Short_history_uses_the_partial_hour_and_stays_low_confidence()
    {
        var start = Now.AddMinutes(-30);
        var history = new[] { new HourlyConsumption(new DateTimeOffset(2026, 10, 12, 12, 0, 0, TimeSpan.Zero), 50m) };

        var rate = ConsumptionRateEstimator.Estimate(history, start, Now, Options);

        Assert.Equal(ForecastConfidence.Low, rate.Confidence);
        Assert.Equal(100m, rate.FlatPerHour); // 50 за полчаса
    }

    [Fact]
    public void Expected_integrates_partial_hours()
    {
        var rate = new ConsumptionRate(12m, null, ForecastConfidence.Medium, 48m);

        Assert.Equal(6m, rate.Expected(Now, Now.AddMinutes(30)));
        Assert.Equal(0m, rate.Expected(Now, Now));
        Assert.Equal(36m, rate.Expected(Now, Now.AddHours(3)));
    }

    private static List<HourlyConsumption> Hours(DateTimeOffset start, int count, Func<int, decimal> qty) =>
        Enumerable.Range(0, count).Select(h => new HourlyConsumption(start.AddHours(h), qty(h))).ToList();
}
