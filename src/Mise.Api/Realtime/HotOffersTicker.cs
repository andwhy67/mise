using Microsoft.Extensions.Options;
using Mise.Api.Infrastructure;
using Mise.Application.HotOffers;
using Mise.Application.Realtime;

namespace Mise.Api.Realtime;

/// <summary>
/// Пересчёт горячих предложений по таймеру (PLAN §4.4): даже без событий срочность растёт с течением времени.
/// Само событие HotOffersChanged уходит, только если выдача изменилась.
/// </summary>
internal sealed class HotOffersTicker(IRealtimeNotifier notifier, RestaurantRunner restaurants, IOptions<HotOfferOptions> options) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(options.Value.RefreshSeconds));
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
                await restaurants.ForEachAsync((_, _) =>
                {
                    notifier.HotOffersMayHaveChanged();
                    return Task.CompletedTask;
                }, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Штатная остановка.
        }
    }
}
