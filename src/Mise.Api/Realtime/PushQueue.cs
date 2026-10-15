using System.Threading.Channels;
using Mise.Application.Abstractions;
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
    private readonly Channel<(Guid RestaurantId, IReadOnlyList<AlertDto> Alerts)> channel =
        Channel.CreateBounded<(Guid, IReadOnlyList<AlertDto>)>(new BoundedChannelOptions(64) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true });

    public void Enqueue(Guid restaurantId, IReadOnlyList<AlertDto> alerts)
    {
        if (alerts.Count > 0)
            channel.Writer.TryWrite((restaurantId, alerts));
    }

    internal ChannelReader<(Guid RestaurantId, IReadOnlyList<AlertDto> Alerts)> Reader => channel.Reader;
}

/// <summary>Фоновая рассылка пушей из <see cref="PushQueue"/>. Ошибка одной пачки не останавливает остальные.</summary>
internal sealed class PushWorker(PushQueue queue, IServiceScopeFactory scopes, ITenant tenant, ILogger<PushWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await foreach (var (restaurantId, alerts) in queue.Reader.ReadAllAsync(stoppingToken))
            {
                try
                {
                    // Подписки — только сотрудников того ресторана, где случился алерт.
                    using var _ = tenant.Use(restaurantId);
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
