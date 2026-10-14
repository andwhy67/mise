using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Mise.Application.Abstractions;
using Mise.Application.Realtime;
using Mise.Domain.Abstractions;
using Mise.Domain.Alerts;
using Mise.Domain.Menu;

namespace Mise.Application.Menu;

/// <summary>
/// Фиксирует переходы блюд в стоп-лист и обратно в <c>stoplist_history</c>.
/// Вызывается после любых изменений остатков и меню; безопасен при параллельных вызовах (advisory lock).
/// </summary>
public sealed class StopListService(
    IMiseDbContext db,
    IStockLocks locks,
    IClock clock,
    IRealtimeNotifier notifier,
    ILogger<StopListService> logger)
{
    private const string LockName = "stoplist-refresh";

    public async Task<StopListChanges> RefreshAsync(CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await locks.AcquireTransactionLockAsync(LockName, ct);

        var now = clock.UtcNow;
        var snapshot = await MenuSnapshot.LoadAsync(db, now, ct);
        var current = snapshot.Dishes
            .Select(d => MenuAvailability.Calculate(d, snapshot.Usable))
            .Where(a => a.InStopList)
            .ToDictionary(a => a.DishId, a => (
                Reason: a.Status == DishAvailabilityStatus.StopManual ? StopReason.Manual : StopReason.Shortage,
                a.LimitingProductId));

        var open = await db.StopListEntries.Where(e => e.EndedAt == null).ToListAsync(ct);
        var added = new List<Guid>();
        var removed = new List<Guid>();

        foreach (var entry in open)
        {
            if (current.TryGetValue(entry.DishId, out var state) && state.Reason == entry.Reason)
                continue;
            entry.Close(now);
            if (!current.ContainsKey(entry.DishId))
                removed.Add(entry.DishId);
        }

        // Сначала закрытия, потом открытия: уникальный индекс «одна открытая запись на блюдо»
        // не должен зависеть от порядка команд внутри одного SaveChanges.
        await db.SaveChangesAsync(ct);

        var stillOpen = open.Where(e => e.IsOpen).Select(e => e.DishId).ToHashSet();
        var wasOpen = open.Select(e => e.DishId).ToHashSet();
        foreach (var (dishId, state) in current.Where(c => !stillOpen.Contains(c.Key)))
        {
            db.StopListEntries.Add(new StopListEntry(Guid.CreateVersion7(now), dishId, state.Reason,
                state.Reason == StopReason.Shortage ? state.LimitingProductId : null, now));
            if (!wasOpen.Contains(dishId))
                added.Add(dishId);
        }

        var alerts = RaiseAlerts(snapshot, current, added, removed, now);

        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        var changes = new StopListChanges(added, removed);
        if (!changes.IsEmpty)
        {
            notifier.StopListChanged(changes);
            notifier.AlertsRaised(alerts.Select(a => a.Id));
        }

        if (added.Count + removed.Count > 0)
            logger.LogInformation("Stop list changed: +{Added} −{Removed}", added.Count, removed.Count);
        return changes;
    }

    /// <summary>Вход и выход блюда из стоп-листа — события для ленты алертов (смена причины без выхода — не событие).</summary>
    private List<Alert> RaiseAlerts(MenuSnapshot snapshot, IReadOnlyDictionary<Guid, (StopReason Reason, Guid? LimitingProductId)> current,
        IEnumerable<Guid> added, IEnumerable<Guid> removed, DateTimeOffset now)
    {
        var dishNames = snapshot.Dishes.ToDictionary(d => d.Id, d => d.Name);
        var alerts = new List<Alert>();
        foreach (var dishId in added)
        {
            var state = current[dishId];
            var limiting = state.Reason == StopReason.Shortage && state.LimitingProductId is { } productId
                ? snapshot.Products.GetValueOrDefault(productId)?.Name
                : null;
            alerts.Add(Alert.StopListOn(Guid.CreateVersion7(now), dishId, dishNames[dishId], limiting, now));
        }

        // Удалённое блюдо уходит из стопа молча — событие «снова доступно» было бы ложным.
        foreach (var dishId in removed.Where(dishNames.ContainsKey))
            alerts.Add(Alert.StopListOff(Guid.CreateVersion7(now), dishId, dishNames[dishId], now));

        db.Alerts.AddRange(alerts);
        return alerts;
    }

    /// <summary>
    /// Пересчёт после уже закоммиченной операции: ошибка здесь не должна превращать успешный заказ/поставку в 500.
    /// Состояние стоп-листа всё равно считается вживую; история догонит при следующем пересчёте.
    /// </summary>
    public async Task<StopListChanges> TryRefreshAsync(CancellationToken ct)
    {
        try
        {
            return await RefreshAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Stop list refresh failed");
            db.ChangeTracker.Clear();
            return StopListChanges.None;
        }
    }
}
