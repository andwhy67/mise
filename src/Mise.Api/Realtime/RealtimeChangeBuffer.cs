using System.Threading.Channels;
using Mise.Application.Abstractions;
using Mise.Application.Menu;
using Mise.Application.Realtime;
using Mise.Application.Simulator;

namespace Mise.Api.Realtime;

/// <summary>
/// Реализация <see cref="IRealtimeNotifier"/>: копит изменения в памяти и будит <see cref="RealtimePublisher"/>.
/// Вызовы дешёвые и не бросают — запрос, который уже закоммитил изменения, не должен падать из-за real-time.
/// </summary>
internal sealed class RealtimeChangeBuffer(ITenant tenant, ILogger<RealtimeChangeBuffer> logger) : IRealtimeNotifier
{
    private readonly Lock _gate = new();
    private readonly Channel<bool> _signal = Channel.CreateBounded<bool>(
        new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropWrite });
    // Изменения копятся отдельно по ресторанам: рассылка — только в группу своего ресторана.
    private Dictionary<Guid, RealtimeChangeSet> _pending = [];

    public ChannelReader<bool> Signals => _signal.Reader;

    /// <summary>Забирает накопленное по всем ресторанам и начинает новое окно.</summary>
    public IReadOnlyDictionary<Guid, RealtimeChangeSet> TakePending()
    {
        lock (_gate)
        {
            var pending = _pending;
            _pending = [];
            return pending;
        }
    }

    public void StockChanged(IEnumerable<Guid> productIds) => Update(c => c.Products.UnionWith(productIds));

    public void BatchStatusChanged(IEnumerable<Guid> batchIds) => Update(c => c.Batches.UnionWith(batchIds));

    public void DishesChanged(IEnumerable<Guid> dishIds) => Update(c => c.Dishes.UnionWith(dishIds));

    public void StopListChanged(StopListChanges changes) => Update(c => c.AddStopListChanges(changes));

    public void AlertsRaised(IEnumerable<Guid> alertIds) => Update(c => c.Alerts.UnionWith(alertIds));

    public void OrderCreated(Guid orderId) => Update(c => c.OrdersCreated.Add(orderId));

    public void OrderStatusChanged(Guid orderId) => Update(c => c.AddOrderUpdated(orderId));

    public void SupplyReceived(Guid supplyId) => Update(c => c.Supplies.Add(supplyId));

    public void SimulatorStateChanged(SimulatorStateDto state) => Update(c => c.Simulator = state);

    public void HotOffersMayHaveChanged() => Update(c => c.HotOffersCheck = true);

    private void Update(Action<RealtimeChangeSet> change)
    {
        if (tenant.RestaurantId is not { } restaurantId)
        {
            // Изменение без ресторана — ошибка вызывающего кода: разослать его некому (и нельзя — всем).
            logger.LogWarning("Realtime change without a restaurant is dropped");
            return;
        }
        bool empty;
        lock (_gate)
        {
            if (!_pending.TryGetValue(restaurantId, out var set))
                _pending[restaurantId] = set = new RealtimeChangeSet();
            change(set);
            empty = set.IsEmpty;
        }
        if (!empty)
            _signal.Writer.TryWrite(true);
    }
}
