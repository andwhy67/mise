using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Mise.Application.Abstractions;
using Mise.Application.Common;
using Mise.Domain.Abstractions;
using Mise.Domain.Catalog;
using Mise.Domain.Expiry;
using Mise.Domain.Stock;

namespace Mise.Application.Stock;

/// <summary>
/// Чтение остатков. Все расчёты срока — относительно IClock; просроченное не входит в usable.
/// Объёмы небольшие (один ресторан): открытые партии читаются целиком и агрегируются в памяти.
/// </summary>
public sealed class StockQueryService(IMiseDbContext db, IClock clock, IOptions<StockOptions> options)
{
    private static readonly BatchStatus[] OpenStatuses = [BatchStatus.Active, BatchStatus.Expired];

    private readonly ShelfLifeThresholds _thresholds = options.Value.ShelfLife.ToThresholds();

    public async Task<IReadOnlyList<StockItemDto>> GetStockAsync(CancellationToken ct)
    {
        var now = clock.UtcNow;
        var products = await db.Products.AsNoTracking().OrderBy(p => p.Name).ToListAsync(ct);
        var batches = (await db.Batches.AsNoTracking()
                .Where(b => OpenStatuses.Contains(b.Status) && b.RemainingQty > 0)
                .ToListAsync(ct))
            .ToLookup(b => b.ProductId);

        return products.Select(p => Summarize(p, batches[p.Id], now)).ToList();
    }

    /// <summary>Сводка только по указанным продуктам (дельта для real-time).</summary>
    public async Task<IReadOnlyList<StockItemDto>> GetStockItemsAsync(IReadOnlyCollection<Guid> productIds, CancellationToken ct)
    {
        if (productIds.Count == 0)
            return [];
        var now = clock.UtcNow;
        var products = await db.Products.AsNoTracking().Where(p => productIds.Contains(p.Id)).OrderBy(p => p.Name).ToListAsync(ct);
        var batches = (await db.Batches.AsNoTracking()
                .Where(b => productIds.Contains(b.ProductId) && OpenStatuses.Contains(b.Status) && b.RemainingQty > 0)
                .ToListAsync(ct))
            .ToLookup(b => b.ProductId);
        return products.Select(p => Summarize(p, batches[p.Id], now)).ToList();
    }

    /// <summary>Партии по id в текущем состоянии (включая закрытые) — дельта для real-time.</summary>
    public Task<IReadOnlyList<BatchDto>> GetBatchesByIdsAsync(IReadOnlyCollection<Guid> batchIds, CancellationToken ct) =>
        batchIds.Count == 0
            ? Task.FromResult<IReadOnlyList<BatchDto>>([])
            : QueryBatchesAsync(new BatchQuery(IncludeClosed: true, Limit: batchIds.Count, BatchIds: batchIds), ct);

    public async Task<ProductBatchesDto> GetProductBatchesAsync(Guid productId, bool includeClosed, CancellationToken ct)
    {
        var product = await db.Products.AsNoTracking().FirstOrDefaultAsync(p => p.Id == productId, ct)
            ?? throw new NotFoundException("Продукт", productId);
        var batches = await QueryBatchesAsync(new BatchQuery(ProductId: productId, IncludeClosed: includeClosed, Limit: 1000), ct);
        var now = clock.UtcNow;
        var open = await db.Batches.AsNoTracking()
            .Where(b => b.ProductId == productId && OpenStatuses.Contains(b.Status) && b.RemainingQty > 0)
            .ToListAsync(ct);
        return new ProductBatchesDto(Summarize(product, open, now), batches);
    }

    public Task<IReadOnlyList<BatchDto>> GetBatchesAsync(BatchQuery query, CancellationToken ct) =>
        QueryBatchesAsync(query with { Limit = Math.Clamp(query.Limit, 1, 1000) }, ct);

    private async Task<IReadOnlyList<BatchDto>> QueryBatchesAsync(BatchQuery query, CancellationToken ct)
    {
        var now = clock.UtcNow;
        var batches = db.Batches.AsNoTracking();
        if (query.ProductId is { } productId)
            batches = batches.Where(b => b.ProductId == productId);
        if (query.BatchIds is { } batchIds)
            batches = batches.Where(b => batchIds.Contains(b.Id));
        if (!query.IncludeClosed)
            batches = batches.Where(b => OpenStatuses.Contains(b.Status) && b.RemainingQty > 0);
        if (query.ExpiringWithinHours is { } hours)
        {
            var horizon = now.AddHours((double)hours);
            batches = batches.Where(b => b.ExpiresAt <= horizon);
        }

        var rows = await (
                from b in batches
                join i in db.SupplyItems on b.SupplyItemId equals i.Id
                join s in db.Supplies on i.SupplyId equals s.Id
                join p in db.Products on b.ProductId equals p.Id
                orderby b.ExpiresAt, b.Id
                select new { Batch = b, SupplyId = s.Id, SupplyNumber = s.Number, p.Name, p.Unit })
            .ToListAsync(ct);

        // FEFO-очерёдность считается по всем пригодным партиям продукта, до фильтра по статусу срока.
        var fefoRank = rows
            .Where(r => r.Batch.IsUsableAt(now))
            .GroupBy(r => r.Batch.ProductId)
            .SelectMany(g => g.Select((r, index) => (r.Batch.Id, Rank: index + 1)))
            .ToDictionary(x => x.Id, x => x.Rank);

        return rows
            .Select(r =>
            {
                var shelf = ShelfLifeClassifier.Classify(r.Batch.ReceivedAt, r.Batch.ExpiresAt, now, _thresholds);
                return new BatchDto(
                    r.Batch.Id, r.Batch.ProductId, r.Name, r.Unit, r.SupplyId, r.SupplyNumber,
                    r.Batch.ReceivedAt, r.Batch.ExpiresAt, r.Batch.ExpirySource,
                    r.Batch.InitialQty, r.Batch.RemainingQty, r.Batch.Status,
                    r.Batch.IsUsableAt(now), ShelfLifeDto.From(shelf),
                    fefoRank.TryGetValue(r.Batch.Id, out var rank) ? rank : null);
            })
            .Where(b => query.ShelfStatuses is not { Count: > 0 } || query.ShelfStatuses.Contains(b.ShelfLife.Status))
            .Take(query.Limit)
            .ToList();
    }

    private StockItemDto Summarize(Product product, IEnumerable<Batch> openBatches, DateTimeOffset now)
    {
        var open = openBatches.ToList();
        var usable = open.Where(b => b.IsUsableAt(now)).OrderBy(b => b.ExpiresAt).ThenBy(b => b.Id).ToList();
        var usableQty = usable.Sum(b => b.RemainingQty);
        var totalQty = open.Sum(b => b.RemainingQty);
        var nearest = usable.FirstOrDefault();

        var status = usable.Count == 0
            ? StockStatus.OutOfStock
            : usable.Select(b => ShelfLifeClassifier.Classify(b.ReceivedAt, b.ExpiresAt, now, _thresholds).Status)
                .Max() switch
            {
                ShelfLifeStatus.Critical => StockStatus.Critical,
                ShelfLifeStatus.Warning => StockStatus.Warning,
                _ => StockStatus.Fresh,
            };

        return new StockItemDto(
            product.Id,
            product.Name,
            product.Unit,
            product.Category,
            usableQty,
            totalQty,
            totalQty - usableQty,
            product.MinStockQty,
            usableQty < product.MinStockQty,
            usable.Count,
            nearest?.ExpiresAt,
            nearest is null ? null : ShelfLifeClassifier.Classify(nearest.ReceivedAt, nearest.ExpiresAt, now, _thresholds).HoursLeft,
            status);
    }
}
