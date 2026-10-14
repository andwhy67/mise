using Mise.Application.Simulator;

namespace Mise.Simulator.Model;

/// <summary>Параметры сценария (PLAN §8). Числа подобраны так, чтобы эффект был виден за минуты демо.</summary>
/// <param name="OrderRateFactor">Множитель потока заказов относительно суточной кривой.</param>
/// <param name="SuppliesEnabled">Плановые поставки по точке заказа.</param>
/// <param name="SpoilageMinutes">Если задано — поставки с явным сроком «через N минут» реального времени (диапазон).</param>
/// <param name="CancelProbability">Вероятность отменить недавний заказ на каждом тике.</param>
/// <param name="DuplicateProbability">Вероятность повторить запрос заказа с тем же Idempotency-Key.</param>
/// <param name="BurstProbability">Вероятность всплеска заказов на тике.</param>
public sealed record ScenarioProfile(
    decimal OrderRateFactor,
    bool SuppliesEnabled,
    (int Min, int Max)? SpoilageMinutes,
    double CancelProbability,
    double DuplicateProbability,
    double BurstProbability,
    int BurstSize)
{
    public static ScenarioProfile For(SimulatorScenario scenario) => scenario switch
    {
        SimulatorScenario.RushHour => new(3m, true, null, 0.01, 0, 0, 0),
        SimulatorScenario.SupplyDelay => new(1.2m, false, null, 0.01, 0, 0, 0),
        SimulatorScenario.Spoilage => new(0.8m, true, (5, 30), 0.01, 0, 0, 0),
        SimulatorScenario.Chaos => new(1.5m, true, null, 0.15, 0.2, 0.1, 8),
        // FreshDelivery — разовая полная поставка при включении, дальше как Normal.
        _ => new(1m, true, null, 0.01, 0, 0, 0),
    };
}

/// <summary>
/// Суточная кривая нагрузки ресторана (заказов в час, время Europe/Moscow): завтрак, пик обеда, пик ужина.
/// </summary>
public static class TrafficModel
{
    public static readonly TimeSpan MoscowOffset = TimeSpan.FromHours(3);

    private static readonly decimal[] OrdersPerHour =
    [
        1, 1, 0, 0, 0, 0, 1, 4,       // 00–07
        8, 8, 6, 10,                  // 08–11 завтраки
        28, 32, 24, 10, 8, 12,        // 12–17 обед
        26, 34, 30, 18, 8, 3,         // 18–23 ужин
    ];

    public static decimal RateAt(DateTimeOffset utc) => OrdersPerHour[utc.ToOffset(MoscowOffset).Hour];

    /// <summary>Ожидаемое число заказов на интервале модельного времени (интеграл кривой по часам).</summary>
    public static decimal Expected(DateTimeOffset from, DateTimeOffset to)
    {
        var expected = 0m;
        var cursor = from;
        while (cursor < to)
        {
            var hourEnd = new DateTimeOffset(cursor.Year, cursor.Month, cursor.Day, cursor.Hour, 0, 0, cursor.Offset).AddHours(1);
            var end = hourEnd < to ? hourEnd : to;
            expected += RateAt(cursor) * (decimal)(end - cursor).TotalHours;
            cursor = end;
        }
        return expected;
    }
}

public static class Poisson
{
    /// <summary>Пуассоновская выборка: алгоритм Кнута для малых λ, нормальное приближение для больших.</summary>
    public static int Sample(Random random, double lambda)
    {
        if (lambda <= 0)
            return 0;
        if (lambda > 30)
        {
            var normal = Math.Sqrt(-2 * Math.Log(1 - random.NextDouble())) * Math.Cos(2 * Math.PI * random.NextDouble());
            return Math.Max(0, (int)Math.Round(lambda + Math.Sqrt(lambda) * normal));
        }

        var limit = Math.Exp(-lambda);
        var k = 0;
        var p = 1.0;
        do
        {
            k++;
            p *= random.NextDouble();
        }
        while (p > limit);
        return k - 1;
    }
}
