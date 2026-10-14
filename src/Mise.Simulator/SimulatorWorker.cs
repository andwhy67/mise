using System.Text.Json;
using System.Threading.Channels;
using Microsoft.Extensions.Options;
using Mise.Application.Simulator;
using Mise.Simulator.Engine;
using StackExchange.Redis;

namespace Mise.Simulator;

/// <summary>Настройки процесса симулятора (секция "Simulator").</summary>
public sealed class SimulatorOptions
{
    public const string SectionName = "Simulator";

    /// <summary>Базовый адрес API (в compose — http://api:8080).</summary>
    public string ApiBaseUrl { get; set; } = "http://localhost:5080";

    /// <summary>Период реального тика, мс.</summary>
    public int TickMilliseconds { get; set; } = 1000;

    /// <summary>Запуститься сразу, если управление ещё ни разу не задавалось.</summary>
    public bool AutoStart { get; set; }

    public string InstanceId { get; set; } = Environment.MachineName;
}

/// <summary>
/// Цикл симулятора: читает желаемое состояние из Redis (и просыпается сразу по каналу управления),
/// выполняет тик модели и публикует heartbeat со статистикой (ключ с TTL + канал для API).
/// </summary>
public sealed class SimulatorWorker(
    IConnectionMultiplexer redis,
    SimulationEngine engine,
    IOptions<SimulatorOptions> options,
    TimeProvider time,
    ILogger<SimulatorWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;
        var db = redis.GetDatabase();
        var wake = Channel.CreateBounded<bool>(new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropWrite });

        await TryAutoStartAsync(db, settings);
        var subscription = await redis.GetSubscriber().SubscribeAsync(RedisChannel.Literal(SimulatorProtocol.ControlChannel));
        subscription.OnMessage(_ => wake.Writer.TryWrite(true));
        logger.LogInformation("Simulator {Instance} ready, API {Api}", settings.InstanceId, settings.ApiBaseUrl);

        var tick = TimeSpan.FromMilliseconds(settings.TickMilliseconds);
        var last = time.GetTimestamp();
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                await ApplyControlAsync(db);
                var elapsed = time.GetElapsedTime(last);
                last = time.GetTimestamp();
                await engine.TickAsync(elapsed, stoppingToken);
                await PublishStatusAsync(db, settings.InstanceId);

                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                timeout.CancelAfter(tick);
                try
                {
                    await wake.Reader.ReadAsync(timeout.Token);
                }
                catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested)
                {
                    // Обычный тик по таймеру.
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Штатная остановка.
        }
    }

    private async Task TryAutoStartAsync(IDatabase db, SimulatorOptions settings)
    {
        if (!settings.AutoStart)
            return;
        var control = SimulatorControl.Default with { Running = true, Version = 1, UpdatedAt = time.GetUtcNow() };
        if (await db.StringSetAsync(SimulatorProtocol.ControlKey, JsonSerializer.Serialize(control, SimulatorProtocol.Json), when: When.NotExists))
            logger.LogInformation("Simulator auto-started");
    }

    private async Task ApplyControlAsync(IDatabase db)
    {
        try
        {
            var raw = await db.StringGetAsync(SimulatorProtocol.ControlKey);
            var control = raw.IsNullOrEmpty
                ? SimulatorControl.Default
                : JsonSerializer.Deserialize<SimulatorControl>(raw.ToString(), SimulatorProtocol.Json) ?? SimulatorControl.Default;
            engine.Apply(control);
        }
        catch (Exception ex) when (ex is RedisException or JsonException)
        {
            logger.LogWarning(ex, "Simulator control read failed");
        }
    }

    private async Task PublishStatusAsync(IDatabase db, string instanceId)
    {
        try
        {
            var json = JsonSerializer.Serialize(engine.Snapshot(instanceId), SimulatorProtocol.Json);
            await db.StringSetAsync(SimulatorProtocol.StatusKey, json, SimulatorProtocol.StatusTtl);
            await redis.GetSubscriber().PublishAsync(RedisChannel.Literal(SimulatorProtocol.StatusChannel), json);
        }
        catch (RedisException ex)
        {
            logger.LogWarning(ex, "Simulator status publish failed");
        }
    }
}
