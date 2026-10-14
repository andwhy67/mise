using Mise.Application.Alerts;
using Mise.Application.Menu;
using Mise.Application.Orders;
using Mise.Application.Stock;
using Mise.Application.Supplies;

namespace Mise.Application.Realtime;

// События хаба /hubs/stock (PLAN §7). Имя метода клиента = имя события без суффикса Event.
// Везде — дельты: только затронутые продукты, блюда, партии; полное состояние клиент берёт из REST.

public sealed record StockChangedEvent(IReadOnlyList<StockItemDto> Items);

public sealed record AvailabilityChangedEvent(IReadOnlyList<DishAvailabilityDto> Dishes);

/// <param name="Added">Блюда, вошедшие в стоп-лист (с причиной и ограничивающим продуктом).</param>
/// <param name="Removed">Блюда, вышедшие из стоп-листа.</param>
public sealed record StopListChangedEvent(IReadOnlyList<StopListItemDto> Added, IReadOnlyList<Guid> Removed);

public sealed record AlertRaisedEvent(IReadOnlyList<AlertDto> Alerts);

public sealed record BatchStatusChangedEvent(IReadOnlyList<BatchDto> Batches);

public sealed record OrderEvent(OrderListItemDto Order);

public sealed record SupplyReceivedEvent(SupplyListItemDto Supply);
