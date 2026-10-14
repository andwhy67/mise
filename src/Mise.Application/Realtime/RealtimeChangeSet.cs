using Mise.Application.Menu;
using Mise.Application.Simulator;

namespace Mise.Application.Realtime;

/// <summary>
/// Накопленные за окно дебаунса изменения. Повторы схлопываются (множества), для стоп-листа побеждает последнее
/// состояние блюда: вошло и вышло в одном окне — клиент получит только «вышло».
/// Не потокобезопасен: синхронизацию обеспечивает владелец (буфер в хосте).
/// </summary>
public sealed class RealtimeChangeSet
{
    public HashSet<Guid> Products { get; } = [];
    public HashSet<Guid> Batches { get; } = [];
    public HashSet<Guid> Dishes { get; } = [];
    public HashSet<Guid> StopListAdded { get; } = [];
    public HashSet<Guid> StopListRemoved { get; } = [];
    public HashSet<Guid> Alerts { get; } = [];
    public HashSet<Guid> OrdersCreated { get; } = [];
    public HashSet<Guid> OrdersUpdated { get; } = [];
    public HashSet<Guid> Supplies { get; } = [];
    public SimulatorStateDto? Simulator { get; set; }

    /// <summary>Явный запрос пересчёта горячих предложений (по таймеру).</summary>
    public bool HotOffersCheck { get; set; }

    /// <summary>Изменения, после которых горячие предложения могли поменяться.</summary>
    public bool AffectsHotOffers =>
        HotOffersCheck || Products.Count + Batches.Count + Dishes.Count + StopListAdded.Count + StopListRemoved.Count > 0;

    public bool IsEmpty =>
        Products.Count + Batches.Count + Dishes.Count + StopListAdded.Count + StopListRemoved.Count + Alerts.Count
        + OrdersCreated.Count + OrdersUpdated.Count + Supplies.Count == 0
        && Simulator is null && !HotOffersCheck;

    public void AddStopListChanges(StopListChanges changes)
    {
        foreach (var dishId in changes.Added)
        {
            StopListRemoved.Remove(dishId);
            StopListAdded.Add(dishId);
        }
        foreach (var dishId in changes.Removed)
        {
            StopListAdded.Remove(dishId);
            StopListRemoved.Add(dishId);
        }
    }

    public void AddOrderUpdated(Guid orderId)
    {
        // Созданный в этом же окне заказ уйдёт событием OrderCreated уже в актуальном статусе.
        if (!OrdersCreated.Contains(orderId))
            OrdersUpdated.Add(orderId);
    }
}
