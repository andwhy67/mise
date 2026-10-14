using Mise.Application.Alerts;
using Mise.Application.HotOffers;
using Mise.Application.Menu;
using Mise.Application.Orders;
using Mise.Application.Simulator;
using Mise.Application.Stock;
using Mise.Application.Supplies;

namespace Mise.Application.Realtime;

/// <summary>Готовые к отправке события одного окна; null/пусто — событие не отправляется.</summary>
public sealed record RealtimePayload(
    SupplyReceivedEvent[] SuppliesReceived,
    OrderEvent[] OrdersCreated,
    OrderEvent[] OrdersUpdated,
    StockChangedEvent? Stock,
    BatchStatusChangedEvent? Batches,
    AvailabilityChangedEvent? Availability,
    StopListChangedEvent? StopList,
    AlertRaisedEvent? Alerts,
    HotOffersDto? HotOffers,
    SimulatorStateDto? Simulator);

/// <summary>
/// Превращает набор «что изменилось» в события с актуальным состоянием из БД на момент отправки
/// (поэтому порядок коммитов внутри окна не важен — клиент получает последнее состояние).
/// </summary>
public sealed class RealtimePayloadBuilder(
    StockQueryService stock,
    MenuQueryService menu,
    AlertService alerts,
    OrderService orders,
    SupplyService supplies,
    HotOfferService hotOffers)
{
    public async Task<RealtimePayload> BuildAsync(RealtimeChangeSet changes, CancellationToken ct)
    {
        var supplyItems = await supplies.GetListItemsAsync(changes.Supplies, ct);
        var created = await orders.GetListItemsAsync(changes.OrdersCreated, ct);
        var updated = await orders.GetListItemsAsync(changes.OrdersUpdated, ct);
        var stockItems = await stock.GetStockItemsAsync(changes.Products, ct);
        var batches = await stock.GetBatchesByIdsAsync(changes.Batches, ct);

        var dishIds = changes.Dishes.Concat(changes.StopListAdded).Concat(changes.StopListRemoved).ToHashSet();
        var availability = await menu.GetAvailabilityForAsync(changes.Products, dishIds, ct);

        StopListChangedEvent? stopList = null;
        if (changes.StopListAdded.Count + changes.StopListRemoved.Count > 0)
        {
            var added = changes.StopListAdded.Count == 0
                ? []
                : (await menu.GetStopListAsync(ct)).Where(s => changes.StopListAdded.Contains(s.DishId)).ToList();
            stopList = new StopListChangedEvent(added, changes.StopListRemoved.ToList());
        }

        var alertItems = await alerts.GetByIdsAsync(changes.Alerts, ct);
        var hotOffersChanged = changes.AffectsHotOffers ? await hotOffers.GetIfChangedAsync(ct) : null;

        return new RealtimePayload(
            supplyItems.Select(s => new SupplyReceivedEvent(s)).ToArray(),
            created.Select(o => new OrderEvent(o)).ToArray(),
            updated.Select(o => new OrderEvent(o)).ToArray(),
            stockItems.Count > 0 ? new StockChangedEvent(stockItems) : null,
            batches.Count > 0 ? new BatchStatusChangedEvent(batches) : null,
            availability.Count > 0 ? new AvailabilityChangedEvent(availability) : null,
            stopList,
            alertItems.Count > 0 ? new AlertRaisedEvent(alertItems) : null,
            hotOffersChanged,
            changes.Simulator);
    }
}
