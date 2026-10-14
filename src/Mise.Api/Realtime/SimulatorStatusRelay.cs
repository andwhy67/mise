using Mise.Application.Realtime;
using Mise.Application.Simulator;
using StackExchange.Redis;

namespace Mise.Api.Realtime;

/// <summary>
/// Пересылает heartbeat симулятора (канал Redis) клиентам хаба событием SimulatorStateChanged.
/// При нескольких инстансах API событие придёт клиенту от каждого — это полный снимок состояния, повтор безвреден.
/// </summary>
internal sealed class SimulatorStatusRelay(
    IConnectionMultiplexer redis,
    IServiceScopeFactory scopes,
    IRealtimeNotifier notifier,
    ILogger<SimulatorStatusRelay> logger) : BackgroundService
{
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var channel = RedisChannel.Literal(SimulatorProtocol.StatusChannel);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var queue = await redis.GetSubscriber().SubscribeAsync(channel);
                queue.OnMessage(_ => RelayAsync(stoppingToken));
                await Task.Delay(Timeout.Infinite, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Simulator status subscription failed, retrying in {Delay}", RetryDelay);
                try
                {
                    await Task.Delay(RetryDelay, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }
    }

    private async Task RelayAsync(CancellationToken ct)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            notifier.SimulatorStateChanged(await scope.ServiceProvider.GetRequiredService<SimulatorService>().GetStateAsync(ct));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Simulator status relay failed");
        }
    }
}
