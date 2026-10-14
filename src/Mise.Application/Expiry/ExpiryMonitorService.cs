using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Mise.Application.Abstractions;
using Mise.Application.Menu;
using Mise.Application.Realtime;
using Mise.Application.Stock;
using Mise.Domain.Abstractions;
using Mise.Domain.Alerts;
using Mise.Domain.Expiry;
using Mise.Domain.Stock;

namespace Mise.Application.Expiry;

/// <summary>
/// Один тик мониторинга сроков (PLAN §4.3): сигналы по порогам, перевод в Expired, автосписание, LowStock,
/// затем пересчёт стоп-листа. Идемпотентен; при нескольких инстансах работает только один (advisory lock).
/// </summary>
public sealed class ExpiryMonitorService(
    IMiseDbContext db,
    IStockLocks locks,
    IClock clock,
    StopListService stopList,
    IRealtimeNotifier notifier,
    IOptions<StockOptions> stockOptions,
    IOptions<ExpiryOptions> expiryOptions,
    ILogger<ExpiryMonitorService> logger)
{
    public const string LockName = "expiry-worker";

    public async Task<ExpiryRunResult> RunOnceAsync(CancellationToken ct)
    {
        var result = await ScanAsync(ct);
        if (result.Skipped)
            return result;

        // Пересчёт и при пустом тике: блюдо могло уйти в стоп просто потому, что истекла партия (§4.1, «по таймеру»).
        var changes = await stopList.TryRefreshAsync(ct);
        result = result with { StopList = changes };
        if (result.HasChanges)
            logger.LogInformation(
                "Expiry tick: expired {Expired}, written off {WrittenOff}, alerts +{Raised} resolved {Resolved}, stop list +{Added} −{Removed}",
                result.Expired, result.WrittenOff, result.AlertsRaised, result.AlertsResolved, changes.Added.Count, changes.Removed.Count);
        return result;
    }

    private async Task<ExpiryRunResult> ScanAsync(CancellationToken ct)
    {
        var options = expiryOptions.Value;
        var thresholds = stockOptions.Value.ShelfLife.ToThresholds();

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        if (!await locks.TryAcquireTransactionLockAsync(LockName, ct))
            return ExpiryRunResult.SkippedRun;

        var now = clock.UtcNow;
        var products = await db.Products.AsNoTracking().ToDictionaryAsync(p => p.Id, ct);

        // Кандидаты на закрытие — под FOR UPDATE; партии в окне предупреждения — только чтение (статус не меняется).
        var closing = await locks.LockExpiryCandidatesAsync(now, options.AutoWriteOff, options.BatchSize, ct);
        var horizon = now.AddHours((double)thresholds.WarningHours);
        var watched = await db.Batches.AsNoTracking()
            .Where(b => b.Status == BatchStatus.Active && b.RemainingQty > 0 && b.ExpiresAt > now && b.ExpiresAt <= horizon)
            .ToListAsync(ct);
        // Без автосписания просроченные остатки не блокируем каждый тик — только поднимаем по ним алерт один раз
        // (в том числе по партиям, принятым уже просроченными).
        List<Batch> unsignalledExpired = options.AutoWriteOff
            ? []
            : await db.Batches.AsNoTracking()
                .Where(b => b.Status == BatchStatus.Expired && b.RemainingQty > 0
                    && !db.Alerts.Any(a => a.BatchId == b.Id && a.Type == AlertType.Expired))
                .Take(options.BatchSize)
                .ToListAsync(ct);
        var batches = closing.Concat(watched).Concat(unsignalledExpired).ToList();

        var batchIds = batches.Select(b => b.Id).ToList();
        var raised = (await db.Alerts.AsNoTracking()
                .Where(a => a.BatchId != null && batchIds.Contains(a.BatchId.Value) && a.DedupKey != null)
                .Select(a => a.DedupKey!)
                .ToListAsync(ct))
            .ToHashSet();

        var plan = ExpiryScanner.Plan(batches, raised, now, thresholds, options.AutoWriteOff);

        foreach (var batch in plan.ToExpire)
            batch.MarkExpired(now);

        var writtenOff = new Dictionary<Guid, decimal>();
        foreach (var batch in plan.ToWriteOff)
        {
            var qty = batch.WriteOff(null);
            writtenOff[batch.Id] = qty;
            db.StockMovements.Add(StockMovement.ExpiryWriteOff(Guid.CreateVersion7(now), batch, qty, now));
        }

        var byId = batches.ToDictionary(b => b.Id);
        var raisedAlerts = new List<Alert>();
        foreach (var signal in plan.Signals)
        {
            var product = products[signal.ProductId];
            raisedAlerts.Add(signal.Level == ShelfLifeStatus.Expired
                ? Alert.Expired(Guid.CreateVersion7(now), signal.BatchId, signal.ProductId, product.Name,
                    writtenOff.TryGetValue(signal.BatchId, out var qty) ? qty : byId[signal.BatchId].RemainingQty, product.Unit,
                    writtenOff.ContainsKey(signal.BatchId), now)
                : Alert.ExpiringSoon(Guid.CreateVersion7(now), signal.BatchId, signal.ProductId, product.Name, signal.Level,
                    signal.HoursLeft, now));
        }
        db.Alerts.AddRange(raisedAlerts);

        // Сначала фиксируем партии: дальнейшие запросы в этой транзакции видят их новое состояние.
        await db.SaveChangesAsync(ct);

        var resolved = await ResolveStaleExpiringAsync(now, ct);
        var (lowRaised, lowResolved) = await SyncLowStockAsync(products.Values, now, ct);
        raisedAlerts.AddRange(lowRaised);

        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        var changedBatches = plan.ToExpire.Concat(plan.ToWriteOff).DistinctBy(b => b.Id).ToList();
        notifier.BatchStatusChanged(changedBatches.Select(b => b.Id));
        notifier.StockChanged(changedBatches.Select(b => b.ProductId).Distinct());
        notifier.AlertsRaised(raisedAlerts.Select(a => a.Id));

        return new ExpiryRunResult(
            Skipped: false,
            Expired: plan.ToExpire.Count,
            WrittenOff: plan.ToWriteOff.Count,
            AlertsRaised: raisedAlerts.Count,
            AlertsResolved: resolved + lowResolved,
            StopList: StopListChanges.None);
    }

    /// <summary>«Скоро истечёт» неактуально, когда партия израсходована, списана или уже просрочена.</summary>
    private async Task<int> ResolveStaleExpiringAsync(DateTimeOffset now, CancellationToken ct)
    {
        var open = await db.Alerts
            .Where(a => a.Type == AlertType.ExpiringSoon && a.ResolvedAt == null && a.BatchId != null)
            .ToListAsync(ct);
        if (open.Count == 0)
            return 0;

        var ids = open.Select(a => a.BatchId!.Value).Distinct().ToList();
        var usable = (await db.Batches.AsNoTracking()
                .Where(b => ids.Contains(b.Id) && b.Status == BatchStatus.Active && b.RemainingQty > 0 && b.ExpiresAt > now)
                .Select(b => b.Id)
                .ToListAsync(ct))
            .ToHashSet();

        var stale = open.Where(a => !usable.Contains(a.BatchId!.Value)).ToList();
        stale.ForEach(a => a.Resolve(now));
        return stale.Count;
    }

    /// <summary>Один открытый LowStock на продукт: открывается при usable &lt; min_stock_qty, закрывается при восстановлении.</summary>
    private async Task<(List<Alert> Raised, int Resolved)> SyncLowStockAsync(IEnumerable<Domain.Catalog.Product> products, DateTimeOffset now,
        CancellationToken ct)
    {
        var usable = await MenuSnapshot.LoadUsableAsync(db, now, ct);
        var open = (await db.Alerts.Where(a => a.Type == AlertType.LowStock && a.ResolvedAt == null).ToListAsync(ct))
            .Where(a => a.ProductId is not null)
            .ToDictionary(a => a.ProductId!.Value);

        var raised = new List<Alert>();
        var resolved = 0;
        foreach (var product in products)
        {
            var qty = usable.GetValueOrDefault(product.Id);
            var isLow = product.MinStockQty > 0 && qty < product.MinStockQty;
            var existing = open.GetValueOrDefault(product.Id);
            open.Remove(product.Id);

            if (isLow && existing is null)
            {
                var alert = Alert.LowStock(Guid.CreateVersion7(now), product.Id, product.Name, qty, product.MinStockQty, product.Unit, now);
                db.Alerts.Add(alert);
                raised.Add(alert);
            }
            else if (!isLow && existing is not null)
            {
                existing.Resolve(now);
                resolved++;
            }
        }

        // Продукт удалён из словаря — его алерт больше не актуален.
        foreach (var orphan in open.Values)
        {
            orphan.Resolve(now);
            resolved++;
        }

        return (raised, resolved);
    }
}

/// <param name="Skipped">Тик пропущен: его уже выполняет другой инстанс (advisory lock занят).</param>
public sealed record ExpiryRunResult(
    bool Skipped,
    int Expired,
    int WrittenOff,
    int AlertsRaised,
    int AlertsResolved,
    StopListChanges StopList)
{
    public static ExpiryRunResult SkippedRun { get; } = new(true, 0, 0, 0, 0, StopListChanges.None);

    public bool HasChanges => Expired + WrittenOff + AlertsRaised + AlertsResolved > 0 || !StopList.IsEmpty;
}
