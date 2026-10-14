using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;
using Mise.Application.Realtime;

namespace Mise.Api.Realtime;

/// <summary>Настройки real-time (секция "Realtime").</summary>
public sealed class RealtimeOptions
{
    public const string SectionName = "Realtime";

    /// <summary>Окно склейки изменений перед отправкой, мс (PLAN §7: группировать, не заливать клиента).</summary>
    public int DebounceMilliseconds { get; set; } = 250;

    public bool IsValid() => DebounceMilliseconds is >= 0 and <= 10_000;
}

/// <summary>
/// Раз в окно дебаунса забирает накопленные изменения, читает актуальное состояние и рассылает события всем клиентам.
/// С Redis backplane событие доходит до клиентов всех инстансов API.
/// </summary>
internal sealed class RealtimePublisher(
    RealtimeChangeBuffer buffer,
    IServiceScopeFactory scopes,
    IHubContext<StockHub, IStockClient> hub,
    IOptions<RealtimeOptions> options,
    ILogger<RealtimePublisher> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var debounce = TimeSpan.FromMilliseconds(options.Value.DebounceMilliseconds);
        try
        {
            while (await buffer.Signals.WaitToReadAsync(stoppingToken))
            {
                buffer.Signals.TryRead(out _);
                if (debounce > TimeSpan.Zero)
                    await Task.Delay(debounce, stoppingToken);

                var changes = buffer.TakePending();
                if (!changes.IsEmpty)
                    await PublishAsync(changes, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Штатная остановка.
        }
    }

    private async Task PublishAsync(RealtimeChangeSet changes, CancellationToken ct)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var payload = await scope.ServiceProvider.GetRequiredService<RealtimePayloadBuilder>().BuildAsync(changes, ct);
            var clients = hub.Clients.All;

            foreach (var supply in payload.SuppliesReceived)
                await clients.SupplyReceived(supply);
            foreach (var order in payload.OrdersCreated)
                await clients.OrderCreated(order);
            foreach (var order in payload.OrdersUpdated)
                await clients.OrderStatusChanged(order);
            if (payload.Stock is { } stock)
                await clients.StockChanged(stock);
            if (payload.Batches is { } batches)
                await clients.BatchStatusChanged(batches);
            if (payload.Availability is { } availability)
                await clients.AvailabilityChanged(availability);
            if (payload.StopList is { } stopList)
                await clients.StopListChanged(stopList);
            if (payload.Alerts is { } alerts)
                await clients.AlertRaised(alerts);
            if (payload.Simulator is { } simulator)
                await clients.SimulatorStateChanged(simulator);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Клиенты догонят состояние через REST; следующее окно отправится как обычно.
            logger.LogError(ex, "Realtime publish failed");
        }
    }
}
