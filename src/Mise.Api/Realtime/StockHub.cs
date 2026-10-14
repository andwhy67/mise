using Microsoft.AspNetCore.SignalR;
using Mise.Application.HotOffers;
using Mise.Application.Realtime;
using Mise.Application.Simulator;

namespace Mise.Api.Realtime;

/// <summary>
/// Методы клиента хаба <c>/hubs/stock</c> (PLAN §7). Имя метода = имя события на клиенте:
/// <c>connection.on("StockChanged", e =&gt; …)</c>.
/// </summary>
public interface IStockClient
{
    Task StockChanged(StockChangedEvent e);
    Task AvailabilityChanged(AvailabilityChangedEvent e);
    Task StopListChanged(StopListChangedEvent e);
    Task AlertRaised(AlertRaisedEvent e);
    Task BatchStatusChanged(BatchStatusChangedEvent e);
    Task OrderCreated(OrderEvent e);
    Task OrderStatusChanged(OrderEvent e);
    Task SupplyReceived(SupplyReceivedEvent e);
    Task SimulatorStateChanged(SimulatorStateDto e);
    Task HotOffersChanged(HotOffersDto e);
}

/// <summary>Хаб только для рассылки сервер → клиенты; состояние клиент берёт из REST (в т.ч. после реконнекта).</summary>
public sealed class StockHub : Hub<IStockClient>
{
    public const string Path = "/hubs/stock";
}
