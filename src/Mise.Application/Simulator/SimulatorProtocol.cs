using System.Text.Json;
using System.Text.Json.Serialization;

// Протокол API ↔ симулятор через Redis. Файл подключается в Mise.Simulator ссылкой (Compose Link):
// только BCL, никаких зависимостей от остальных слоёв.
namespace Mise.Application.Simulator;

public enum SimulatorScenario
{
    /// <summary>Обычный день: заказы по суточной кривой, поставки по расписанию.</summary>
    Normal,
    /// <summary>Час пик: заказов втрое больше.</summary>
    RushHour,
    /// <summary>Поставки задерживаются — склад идёт к дефициту и стоп-листу.</summary>
    SupplyDelay,
    /// <summary>Поставки с очень коротким сроком — видно сигналы и автосписание.</summary>
    Spoilage,
    /// <summary>Сразу крупная свежая поставка по всем продуктам, дальше как Normal.</summary>
    FreshDelivery,
    /// <summary>Всплески заказов, отмены, повторы с тем же Idempotency-Key.</summary>
    Chaos,
}

/// <summary>Желаемое состояние симулятора — пишет API (<see cref="Version"/> растёт при каждом изменении).</summary>
public sealed record SimulatorControl(
    bool Running,
    SimulatorScenario Scenario,
    int Speed,
    int? Seed,
    long Version,
    DateTimeOffset? UpdatedAt)
{
    public static SimulatorControl Default { get; } = new(false, SimulatorScenario.Normal, 1, null, 0, null);
}

public sealed record SimulatorCounters(
    int OrdersCreated,
    int OrdersRejected,
    int OrdersReplayed,
    int OrdersCancelled,
    int OrdersCompleted,
    int Supplies,
    int SupplyItems,
    int Retries,
    int Errors)
{
    public static SimulatorCounters Empty { get; } = new(0, 0, 0, 0, 0, 0, 0, 0, 0);
}

/// <param name="Kind">order, rejected, supply, cancel, replay, error, scenario.</param>
public sealed record SimulatorActivity(DateTimeOffset At, string Kind, string Message);

/// <summary>Фактическое состояние — пишет симулятор раз в секунду (heartbeat с TTL).</summary>
/// <param name="AppliedVersion">Версия <see cref="SimulatorControl"/>, которую симулятор уже применил.</param>
/// <param name="SimulatedTime">Модельное время (ускоряется в <see cref="Speed"/> раз).</param>
public sealed record SimulatorStatus(
    string InstanceId,
    bool Running,
    SimulatorScenario Scenario,
    int Speed,
    long AppliedVersion,
    DateTimeOffset HeartbeatAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? SimulatedTime,
    SimulatorCounters Counters,
    IReadOnlyList<SimulatorActivity> Recent);

public static class SimulatorProtocol
{
    public const string ControlKey = "mise:simulator:control";
    public const string StatusKey = "mise:simulator:status";
    public const string ControlChannel = "mise:simulator:control";
    public const string StatusChannel = "mise:simulator:status";

    public const int MinSpeed = 1;
    public const int MaxSpeed = 60;

    /// <summary>Нет heartbeat дольше TTL — ключ статуса исчезает, симулятор считается офлайн.</summary>
    public static readonly TimeSpan StatusTtl = TimeSpan.FromSeconds(15);

    public static JsonSerializerOptions Json { get; } = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };
}
