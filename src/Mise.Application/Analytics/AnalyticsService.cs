using FluentValidation.Results;
using Microsoft.EntityFrameworkCore;
using Mise.Application.Abstractions;
using Mise.Application.Common;
using Mise.Application.Menu;
using Mise.Domain.Abstractions;
using Mise.Domain.Alerts;
using Mise.Domain.Forecasting;
using Mise.Domain.Orders;
using Mise.Domain.Stock;

namespace Mise.Application.Analytics;

/// <summary>Аналитика (PLAN §4.7): списания, дефицит, расход, KPI. Объёмы — один ресторан, агрегация в памяти.</summary>
public sealed class AnalyticsService(IMiseDbContext db, IClock clock, IConsumptionAggregator aggregator, MenuQueryService menu)
{
    public const int DefaultDays = 7;
    public const int MaxDays = 92;
    private static readonly MovementType[] WriteOffTypes = [MovementType.ExpiryWriteOff, MovementType.ManualWriteOff];

    public AnalyticsPeriod ResolvePeriod(DateTimeOffset? from, DateTimeOffset? to)
    {
        var end = (to ?? clock.UtcNow).ToUniversalTime();
        var start = (from ?? end.AddDays(-DefaultDays)).ToUniversalTime();
        if (start >= end)
            throw new[] { new ValidationFailure("from", "Начало периода должно быть раньше конца.") }.ToValidationException();
        if (end - start > TimeSpan.FromDays(MaxDays))
            throw new[] { new ValidationFailure("from", $"Период — не больше {MaxDays} дней.") }.ToValidationException();
        return new AnalyticsPeriod(start, end);
    }

    public async Task<WriteOffReportDto> GetWriteOffsAsync(AnalyticsPeriod period, CancellationToken ct)
    {
        var rows = await (
                from m in db.StockMovements.AsNoTracking()
                where WriteOffTypes.Contains(m.Type) && m.CreatedAt >= period.From && m.CreatedAt < period.To
                join p in db.Products on m.ProductId equals p.Id
                select new { m.Type, m.ProductId, p.Name, p.Unit, m.CreatedAt, Qty = -m.QtyDelta })
            .ToListAsync(ct);

        var items = rows
            .GroupBy(r => (Date: MoscowTime.DateOf(r.CreatedAt), r.ProductId, r.Name, r.Unit, r.Type))
            .Select(g => new WriteOffItemDto(g.Key.Date, g.Key.ProductId, g.Key.Name, g.Key.Unit, g.Key.Type, g.Sum(r => r.Qty), g.Count()))
            .OrderBy(i => i.Date).ThenBy(i => i.ProductName).ThenBy(i => i.Reason)
            .ToList();
        var byProduct = rows
            .GroupBy(r => (r.ProductId, r.Name, r.Unit))
            .Select(g => new WriteOffByProductDto(g.Key.ProductId, g.Key.Name, g.Key.Unit,
                g.Where(r => r.Type == MovementType.ExpiryWriteOff).Sum(r => r.Qty),
                g.Where(r => r.Type == MovementType.ManualWriteOff).Sum(r => r.Qty),
                g.Sum(r => r.Qty), g.Count()))
            .OrderByDescending(p => p.Operations).ThenBy(p => p.ProductName)
            .ToList();
        var byDay = rows
            .GroupBy(r => MoscowTime.DateOf(r.CreatedAt))
            .Select(g => new WriteOffByDayDto(g.Key,
                g.Count(r => r.Type == MovementType.ExpiryWriteOff),
                g.Count(r => r.Type == MovementType.ManualWriteOff),
                g.Select(r => r.ProductId).Distinct().Count()))
            .OrderBy(d => d.Date)
            .ToList();

        return new WriteOffReportDto(period.From, period.To,
            rows.Count(r => r.Type == MovementType.ExpiryWriteOff), rows.Count(r => r.Type == MovementType.ManualWriteOff),
            byDay, byProduct, items);
    }

    public async Task<DeficitReportDto> GetDeficitAsync(AnalyticsPeriod period, CancellationToken ct)
    {
        var now = clock.UtcNow;
        var stops = await db.StopListEntries.AsNoTracking()
            .Where(e => e.LimitingProductId != null && e.StartedAt < period.To && (e.EndedAt == null || e.EndedAt > period.From))
            .Select(e => new { ProductId = e.LimitingProductId!.Value, e.DishId, e.StartedAt, e.EndedAt })
            .ToListAsync(ct);
        var lows = await db.Alerts.AsNoTracking()
            .Where(a => a.Type == AlertType.LowStock && a.ProductId != null && a.CreatedAt < period.To
                && (a.ResolvedAt == null || a.ResolvedAt > period.From))
            .Select(a => new { ProductId = a.ProductId!.Value, a.CreatedAt, a.ResolvedAt })
            .ToListAsync(ct);

        // Отказы в заказах из-за продукта (422): упущенные продажи — самый прямой признак дефицита.
        var refusals = await db.OrderShortages.AsNoTracking()
            .Where(r => r.CreatedAt >= period.From && r.CreatedAt < period.To)
            .GroupBy(r => r.ProductId)
            .Select(g => new { ProductId = g.Key, Count = g.Count(), Qty = g.Sum(r => r.Shortage), Last = g.Max(r => r.CreatedAt) })
            .ToDictionaryAsync(r => r.ProductId, ct);

        var productIds = stops.Select(s => s.ProductId).Concat(lows.Select(l => l.ProductId)).Concat(refusals.Keys).Distinct().ToList();
        var products = await db.Products.AsNoTracking().Where(p => productIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, p => new { p.Name, p.Unit }, ct);

        var items = productIds
            .Where(products.ContainsKey)
            .Select(id =>
            {
                var productStops = stops.Where(s => s.ProductId == id).ToList();
                var minutesBelow = lows.Where(l => l.ProductId == id).Sum(l => Overlap(period, l.CreatedAt, l.ResolvedAt ?? now));
                var refused = refusals.GetValueOrDefault(id);
                return new DeficitItemDto(id, products[id].Name, products[id].Unit,
                    refused?.Count ?? 0,
                    refused?.Qty ?? 0m,
                    refused?.Last,
                    productStops.Count(s => s.StartedAt >= period.From),
                    productStops.Select(s => s.DishId).Distinct().Count(),
                    decimal.Round(productStops.Sum(s => Overlap(period, s.StartedAt, s.EndedAt ?? now)), 1),
                    decimal.Round(minutesBelow, 1),
                    decimal.Round(Math.Min(1m, minutesBelow / period.Minutes), 4));
            })
            // Сначала то, из-за чего уже теряли заказы, затем — сколько держало блюда в стопе.
            .OrderByDescending(i => i.Refusals).ThenByDescending(i => i.DishMinutesInStop).ThenByDescending(i => i.StopEvents)
            .ThenByDescending(i => i.MinutesBelowMinimum)
            .ThenBy(i => i.ProductName)
            .ToList();

        return new DeficitReportDto(period.From, period.To, items);
    }

    public async Task<ConsumptionReportDto> GetConsumptionAsync(AnalyticsPeriod period, ConsumptionBucket bucket, Guid? productId,
        int limit, CancellationToken ct)
    {
        await aggregator.RefreshAsync(ct);
        // Корзины, пересекающиеся с периодом: час 13:00 входит в период с 13:40 (расход 13:40–14:00 в нём же).
        var firstBucket = ConsumptionRateEstimator.FloorToHour(period.From);
        var query = db.ConsumptionHours.AsNoTracking().Where(c => c.HourBucket >= firstBucket && c.HourBucket < period.To);
        if (productId is { } id)
            query = query.Where(c => c.ProductId == id);
        var rows = await (
                from c in query
                join p in db.Products on c.ProductId equals p.Id
                select new { c.ProductId, p.Name, p.Unit, c.HourBucket, c.QtyConsumed })
            .ToListAsync(ct);

        var series = rows
            .GroupBy(r => (r.ProductId, r.Name, r.Unit))
            .Select(g => new ConsumptionSeriesDto(g.Key.ProductId, g.Key.Name, g.Key.Unit, g.Sum(r => r.QtyConsumed),
                g.GroupBy(r => bucket == ConsumptionBucket.Day ? MoscowTime.DayStart(MoscowTime.DateOf(r.HourBucket)) : r.HourBucket)
                    .Select(b => new ConsumptionPointDto(b.Key, b.Sum(r => r.QtyConsumed)))
                    .OrderBy(p => p.Bucket)
                    .ToList()))
            .OrderByDescending(s => s.Total).ThenBy(s => s.ProductName)
            .Take(Math.Clamp(limit, 1, 100))
            .ToList();

        return new ConsumptionReportDto(period.From, period.To, bucket, series);
    }

    public async Task<KpiReportDto> GetKpiAsync(AnalyticsPeriod period, CancellationToken ct)
    {
        var now = clock.UtcNow;
        var orders = await db.Orders.AsNoTracking()
            .Where(o => o.CreatedAt >= period.From && o.CreatedAt < period.To)
            .Select(o => new { o.Status, Total = o.Items.Sum(i => i.PriceSnapshot * i.Quantity) })
            .ToListAsync(ct);
        var served = orders.Where(o => o.Status != OrderStatus.Cancelled).ToList();
        var revenue = served.Sum(o => o.Total);

        var flows = await (
                from m in db.StockMovements.AsNoTracking()
                where m.CreatedAt >= period.From && m.CreatedAt < period.To
                      && (m.Type == MovementType.Receipt || m.Type == MovementType.PrepOutput || WriteOffTypes.Contains(m.Type))
                group m by new { m.ProductId, IsReceipt = m.Type == MovementType.Receipt || m.Type == MovementType.PrepOutput } into g
                select new { g.Key.ProductId, g.Key.IsReceipt, Qty = g.Sum(m => m.QtyDelta) })
            .ToListAsync(ct);
        var productIds = flows.Select(f => f.ProductId).Distinct().ToList();
        var products = await db.Products.AsNoTracking().Where(p => productIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, ct);
        var rates = productIds
            .Select(id => (Id: id,
                Received: flows.Where(f => f.ProductId == id && f.IsReceipt).Sum(f => f.Qty),
                WrittenOff: -flows.Where(f => f.ProductId == id && !f.IsReceipt).Sum(f => f.Qty)))
            .Where(x => x.Received > 0 && products.ContainsKey(x.Id))
            .Select(x => new WriteOffRateDto(x.Id, products[x.Id].Name, products[x.Id].Unit, x.Received, x.WrittenOff,
                decimal.Round(x.WrittenOff / x.Received, 4)))
            .ToList();

        var stops = await db.StopListEntries.AsNoTracking()
            .Where(e => e.StartedAt < period.To && (e.EndedAt == null || e.EndedAt > period.From))
            .Select(e => new { e.StartedAt, e.EndedAt })
            .ToListAsync(ct);
        var dishMinutes = stops.Sum(s => Overlap(period, s.StartedAt, s.EndedAt ?? now));

        var periodKpi = new KpiPeriodDto(
            served.Count,
            orders.Count - served.Count,
            revenue,
            served.Count == 0 ? 0m : decimal.Round(revenue / served.Count, 2),
            rates.Count == 0 ? 0m : decimal.Round(rates.Average(r => r.Rate), 4),
            rates.OrderByDescending(r => r.Rate).ThenBy(r => r.ProductName).Take(5).ToList(),
            decimal.Round(dishMinutes, 1),
            decimal.Round(dishMinutes / period.Minutes, 3));

        return new KpiReportDto(period.From, period.To, periodKpi, await GetNowAsync(now, ct));
    }

    private async Task<KpiNowDto> GetNowAsync(DateTimeOffset now, CancellationToken ct)
    {
        var todayStart = MoscowTime.DayStart(MoscowTime.DateOf(now));
        var horizon = now.AddHours(24);
        var expiring = await db.Batches.AsNoTracking()
            .CountAsync(b => b.Status == BatchStatus.Active && b.RemainingQty > 0 && b.ExpiresAt > now && b.ExpiresAt <= horizon, ct);
        var belowMinimum = await db.Alerts.AsNoTracking().CountAsync(a => a.Type == AlertType.LowStock && a.ResolvedAt == null, ct);
        var writeOffs = await db.StockMovements.AsNoTracking()
            .CountAsync(m => WriteOffTypes.Contains(m.Type) && m.CreatedAt >= todayStart && m.CreatedAt <= now, ct);
        var today = await db.Orders.AsNoTracking()
            .Where(o => o.CreatedAt >= todayStart && o.CreatedAt <= now && o.Status != OrderStatus.Cancelled)
            .Select(o => o.Items.Sum(i => i.PriceSnapshot * i.Quantity))
            .ToListAsync(ct);
        var inStop = (await menu.GetStopListAsync(ct)).Count;

        return new KpiNowDto(inStop, expiring, belowMinimum, writeOffs, today.Count, today.Sum());
    }

    /// <summary>Минуты пересечения интервала [start; end) с периодом.</summary>
    private static decimal Overlap(AnalyticsPeriod period, DateTimeOffset start, DateTimeOffset end)
    {
        var from = start > period.From ? start : period.From;
        var to = end < period.To ? end : period.To;
        return to > from ? (decimal)(to - from).TotalMinutes : 0m;
    }
}
