using System.Text.Json;
using Mise.Application.Simulator;
using StackExchange.Redis;

namespace Mise.Infrastructure.Simulator;

/// <summary>
/// Управление симулятором в Redis: желаемое состояние — ключ (меняется оптимистично, с условием на прежнее значение),
/// об изменении симулятор узнаёт сразу из канала; статус — ключ с TTL, который обновляет сам симулятор.
/// </summary>
internal sealed class RedisSimulatorControlStore(IConnectionMultiplexer redis) : ISimulatorControlStore
{
    private const int MaxAttempts = 10;
    private static readonly RedisKey ControlKey = SimulatorProtocol.ControlKey;
    private static readonly RedisKey StatusKey = SimulatorProtocol.StatusKey;

    public async Task<SimulatorControl> GetControlAsync(CancellationToken ct) =>
        Parse<SimulatorControl>(await redis.GetDatabase().StringGetAsync(ControlKey)) ?? SimulatorControl.Default;

    public async Task<SimulatorControl> UpdateControlAsync(Func<SimulatorControl, SimulatorControl> change, CancellationToken ct)
    {
        var db = redis.GetDatabase();
        for (var attempt = 0; attempt < MaxAttempts; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            var raw = await db.StringGetAsync(ControlKey);
            var next = change(Parse<SimulatorControl>(raw) ?? SimulatorControl.Default);
            var json = JsonSerializer.Serialize(next, SimulatorProtocol.Json);

            var transaction = db.CreateTransaction();
            transaction.AddCondition(raw.IsNull ? Condition.KeyNotExists(ControlKey) : Condition.StringEqual(ControlKey, raw));
            _ = transaction.StringSetAsync(ControlKey, json);
            if (await transaction.ExecuteAsync())
            {
                await redis.GetSubscriber().PublishAsync(RedisChannel.Literal(SimulatorProtocol.ControlChannel), json);
                return next;
            }
        }

        throw new InvalidOperationException("Не удалось изменить состояние симулятора: слишком много параллельных изменений.");
    }

    public async Task<SimulatorStatus?> GetStatusAsync(CancellationToken ct) =>
        Parse<SimulatorStatus>(await redis.GetDatabase().StringGetAsync(StatusKey));

    private static T? Parse<T>(RedisValue value) where T : class =>
        value.IsNullOrEmpty ? null : JsonSerializer.Deserialize<T>(value.ToString(), SimulatorProtocol.Json);
}
