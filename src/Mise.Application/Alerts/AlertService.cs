using Microsoft.EntityFrameworkCore;
using Mise.Application.Abstractions;
using Mise.Application.Common;
using Mise.Domain.Abstractions;
using Mise.Domain.Alerts;

namespace Mise.Application.Alerts;

/// <summary>Лента алертов и их подтверждение. Поднимают алерты ExpiryMonitorService и StopListService.</summary>
public sealed class AlertService(IMiseDbContext db, IClock clock)
{
    public async Task<PagedResult<AlertDto>> ListAsync(AlertQuery filter, PageRequest page, CancellationToken ct)
    {
        var query = db.Alerts.AsNoTracking();
        if (filter.Types is { Count: > 0 } types)
            query = query.Where(a => types.Contains(a.Type));
        if (filter.Severities is { Count: > 0 } severities)
            query = query.Where(a => severities.Contains(a.Severity));
        if (filter.Acknowledged is { } acknowledged)
            query = acknowledged ? query.Where(a => a.AcknowledgedAt != null) : query.Where(a => a.AcknowledgedAt == null);
        if (filter.Active is { } active)
            query = active ? query.Where(a => a.ResolvedAt == null) : query.Where(a => a.ResolvedAt != null);
        if (filter.ProductId is { } productId)
            query = query.Where(a => a.ProductId == productId);
        if (filter.BatchId is { } batchId)
            query = query.Where(a => a.BatchId == batchId);
        if (filter.DishId is { } dishId)
            query = query.Where(a => a.DishId == dishId);
        if (filter.Since is { } since)
            query = query.Where(a => a.CreatedAt >= since);

        var total = await query.CountAsync(ct);
        var alerts = await query
            .OrderByDescending(a => a.CreatedAt).ThenByDescending(a => a.Id)
            .Skip(page.Skip).Take(page.SafePageSize)
            .ToListAsync(ct);
        return new PagedResult<AlertDto>(await ToDtosAsync(alerts, ct), total, page.SafePage, page.SafePageSize);
    }

    public async Task<AlertSummaryDto> GetSummaryAsync(CancellationToken ct)
    {
        var counts = await db.Alerts.AsNoTracking()
            .Where(a => a.AcknowledgedAt == null && a.ResolvedAt == null)
            .GroupBy(a => a.Severity)
            .Select(g => new { Severity = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Severity, x => x.Count, ct);
        return new AlertSummaryDto(
            counts.Values.Sum(),
            counts.GetValueOrDefault(AlertSeverity.Critical),
            counts.GetValueOrDefault(AlertSeverity.Warning),
            counts.GetValueOrDefault(AlertSeverity.Info));
    }

    public async Task<IReadOnlyList<AlertDto>> GetByIdsAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct)
    {
        if (ids.Count == 0)
            return [];
        var alerts = await db.Alerts.AsNoTracking().Where(a => ids.Contains(a.Id))
            .OrderByDescending(a => a.CreatedAt).ThenByDescending(a => a.Id)
            .ToListAsync(ct);
        return await ToDtosAsync(alerts, ct);
    }

    /// <summary>Подтверждение идемпотентно: повтор не меняет время первого подтверждения.</summary>
    public async Task<AlertDto> AcknowledgeAsync(Guid id, CancellationToken ct)
    {
        var alert = await db.Alerts.FirstOrDefaultAsync(a => a.Id == id, ct) ?? throw new NotFoundException("Алерт", id);
        if (alert.AcknowledgedAt is null)
        {
            alert.Acknowledge(clock.UtcNow);
            await db.SaveChangesAsync(ct);
        }
        return (await ToDtosAsync([alert], ct))[0];
    }

    private async Task<IReadOnlyList<AlertDto>> ToDtosAsync(IReadOnlyCollection<Alert> alerts, CancellationToken ct)
    {
        var productIds = alerts.Where(a => a.ProductId is not null).Select(a => a.ProductId!.Value).Distinct().ToList();
        var dishIds = alerts.Where(a => a.DishId is not null).Select(a => a.DishId!.Value).Distinct().ToList();
        var products = await db.Products.AsNoTracking().Where(p => productIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, p => p.Name, ct);
        var dishes = await db.Dishes.AsNoTracking().Where(d => dishIds.Contains(d.Id))
            .ToDictionaryAsync(d => d.Id, d => d.Name, ct);

        return alerts
            .Select(a => new AlertDto(
                a.Id, a.Type, a.Severity, a.Message,
                a.BatchId,
                a.ProductId, a.ProductId is { } p ? products.GetValueOrDefault(p) : null,
                a.DishId, a.DishId is { } d ? dishes.GetValueOrDefault(d) : null,
                a.CreatedAt, a.AcknowledgedAt, a.ResolvedAt, a.IsActive))
            .ToList();
    }
}
