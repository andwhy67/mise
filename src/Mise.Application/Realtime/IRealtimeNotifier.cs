using Mise.Application.Menu;
using Mise.Application.Simulator;

namespace Mise.Application.Realtime;

/// <summary>
/// Сообщает real-time слою, ЧТО изменилось (CLAUDE.md, правило 8). Вызывается только после коммита.
/// Методы не блокируют и не бросают: изменения копятся и уходят клиентам пачкой (дебаунс), полезная нагрузка
/// (остатки, доступность, алерты) читается из БД в момент отправки — клиент получает актуальное состояние.
/// </summary>
public interface IRealtimeNotifier
{
    /// <summary>Изменились остатки продуктов → StockChanged + AvailabilityChanged по блюдам с этими продуктами.</summary>
    void StockChanged(IEnumerable<Guid> productIds);

    /// <summary>Изменился статус партий (Depleted, Expired, WrittenOff, снова Active) → BatchStatusChanged.</summary>
    void BatchStatusChanged(IEnumerable<Guid> batchIds);

    /// <summary>Изменилось блюдо (состав, активность) → AvailabilityChanged.</summary>
    void DishesChanged(IEnumerable<Guid> dishIds);

    void StopListChanged(StopListChanges changes);

    void AlertsRaised(IEnumerable<Guid> alertIds);

    void OrderCreated(Guid orderId);

    void OrderStatusChanged(Guid orderId);

    void SupplyReceived(Guid supplyId);

    void SimulatorStateChanged(SimulatorStateDto state);
}
