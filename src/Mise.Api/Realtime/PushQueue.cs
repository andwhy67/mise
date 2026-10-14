using System.Threading.Channels;
using Mise.Application.Alerts;
using Mise.Application.Push;

namespace Mise.Api.Realtime;

/// <summary>
/// Очередь новых алертов на рассылку пушами. Ограниченная: если сервис пушей «лежит», real-time и API
/// не ждут его, а самые старые пачки вытесняются (они уже видны в приложении).
/// Алерт попадает сюда только на инстансе, где он создан, — у каждого инстанса свой буфер изменений, дублей нет.
/// </summary>
public sealed class PushQueue
{
    private readonly Channel<IReadOnlyList<AlertDto>> channel =
        Channel.CreateBounded<IReadOnlyList<AlertDto>>(new BoundedChannelOptions(64) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true });

    public void Enqueue(IReadOnlyList<AlertDto> alerts)
    {
        if (alerts.Count > 0)
            channel.Writer.TryWrite(alerts);
    }

    internal ChannelReader<IReadOnlyList<AlertDto>> Reader => channel.Reader;
}

/// <summary>Фоновая рассылка пушей из <see cref="PushQueue"/>. Ошибка одной пачки не останавливает остальные.</summary>
internal sealed class PushWorker(PushQueue queue, IServiceScopeFactory scopes, ILogger<PushWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await foreach (var alerts in queue.Reader.ReadAllAsync(stoppingToken))
            {
                try
                {
                    await using var scope = scopes.CreateAsyncScope();
                    var sent = await scope.ServiceProvider.GetRequiredService<PushDispatcher>().DispatchAsync(alerts, stoppingToken);
                    if (sent > 0)
                        logger.LogInformation("Push: sent {Count} notifications for {Alerts} alerts", sent, alerts.Count);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogError(ex, "Push dispatch failed");
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Штатная остановка.
        }
    }
}
