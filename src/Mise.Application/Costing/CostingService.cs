using Microsoft.EntityFrameworkCore;
using Mise.Application.Abstractions;
using Mise.Application.Analytics;
using Mise.Domain.Abstractions;
using Mise.Domain.Common;
using Mise.Domain.Costing;
using Mise.Domain.Orders;
using Mise.Domain.Stock;

namespace Mise.Application.Costing;

/// <param name="PortionCost">Себестоимость порции по партиям, которые уйдут в работу первыми; null — цена неизвестна.</param>
/// <param name="Estimated">Часть цены — ориентировочная из справочника (у партии нет цены или продукта нет на складе).</param>
public sealed record DishEconomicsDto(
    Guid DishId,
    string DishName,
    string? Category,
    decimal Price,
    decimal? PortionCost,
    decimal? Margin,
    decimal? FoodCostShare,
    bool Estimated,
    IReadOnlyList<string> UnpricedProducts);

/// <param name="Purchased">Закуплено за период, ₽.</param>
/// <param name="CostOfSales">Себестоимость проданного (расход в заказы минус возвраты по отменам), ₽.</param>
/// <param name="Losses">Потери: списано по сроку и вручную, ₽.</param>
/// <param name="Rescued">Продано из партий на грани срока (≤ окна «спасения»), ₽ — то, что иначе ушло бы в списание.</param>
public sealed record MoneyProductDto(
    Guid ProductId,
    string ProductName,
    MeasureUnit Unit,
    decimal Purchased,
    decimal CostOfSales,
    decimal Losses,
    decimal ExpiryLosses,
    decimal Rescued,
    decimal RescuedQty);

/// <param name="Revenue">Выручка: заказы периода без отменённых.</param>
/// <param name="GrossMargin">Выручка минус себестоимость проданного.</param>
/// <param name="CostCoverage">Доля движений с известной ценой (0…1): меньше 1 — часть сумм неполная.</param>
public sealed record MoneyReportDto(
    DateTimeOffset From,
    DateTimeOffset To,
    decimal Revenue,
    decimal Purchases,
    decimal CostOfSales,
    decimal GrossMargin,
    decimal Losses,
    decimal ExpiryLosses,
    decimal ManualLosses,
    decimal Rescued,
    decimal CostCoverage,
    IReadOnlyList<MoneyProductDto> Products);

/// <summary>
/// Деньги кухни: себестоимость и маржа блюд «на сейчас» и отчёт за период по журналу движений.
/// Суммы берутся из стоимости, записанной в каждом движении в момент операции, — задним числом не пересчитываются.
/// </summary>
public sealed class CostingService(IMiseDbContext db, IClock clock)
{
    public async Task<IReadOnlyList<DishEconomicsDto>> GetDishEconomicsAsync(CancellationToken ct)
    {
        var now = clock.UtcNow;
        var dishes = await db.Dishes.AsNoTracking().Include(d => d.Ingredients).OrderBy(d => d.Name).ToListAsync(ct);
        var products = await db.Products.AsNoTracking().ToDictionaryAsync(p => p.Id, p => new { p.Name, p.ReferenceUnitCost }, ct);
        // Пригодные партии в порядке списания (FEFO: ранний срок первым, затем id) — как их заберёт следующий заказ.
        var batches = await db.Batches.AsNoTracking()
            .Where(b => b.Status == BatchStatus.Active && b.RemainingQty > 0 && b.ExpiresAt > now)
            .OrderBy(b => b.ProductId).ThenBy(b => b.ExpiresAt).ThenBy(b => b.Id)
            .Select(b => new { b.ProductId, b.RemainingQty, b.UnitCost })
            .ToListAsync(ct);
        var lots = batches.GroupBy(b => b.ProductId)
            .SelectMany(g => g.Select((b, i) => new CostLot(b.ProductId, b.RemainingQty, b.UnitCost, i + 1)))
            .ToList();
        var references = products.ToDictionary(p => p.Key, p => p.Value.ReferenceUnitCost);

        return dishes.Select(d =>
        {
            var cost = DishCosting.Cost(d.Ingredients.Select(i => new CostIngredient(i.ProductId, i.Quantity)).ToList(), lots, references);
            var economics = cost.PortionCost is { } c ? DishCosting.Economics(d.Price, c) : null;
            return new DishEconomicsDto(d.Id, d.Name, d.Category, d.Price, cost.PortionCost, economics?.Margin, economics?.FoodCostShare,
                cost.Estimated, cost.UnpricedProducts.Select(id => products.GetValueOrDefault(id)?.Name ?? "?").ToList());
        }).ToList();
    }

    public async Task<MoneyReportDto> GetMoneyAsync(AnalyticsPeriod period, CancellationToken ct)
    {
        var movements = await db.StockMovements.AsNoTracking()
            .Where(m => m.CreatedAt >= period.From && m.CreatedAt < period.To)
            .Select(m => new { m.ProductId, m.BatchId, m.OrderId, m.Type, m.QtyDelta, m.Cost, m.Rescued })
            .ToListAsync(ct);
        var revenue = await db.Orders.AsNoTracking()
            .Where(o => o.CreatedAt >= period.From && o.CreatedAt < period.To && o.Status != OrderStatus.Cancelled)
            .SelectMany(o => o.Items)
            .SumAsync(i => (decimal?)(i.PriceSnapshot * i.Quantity), ct) ?? 0m;

        // Возврат по отмене «отменяет» спасение, если исходный расход из этой партии в этот заказ был спасённым.
        var rescuedPairs = movements.Where(m => m.Type == MovementType.OrderConsumption && m.Rescued)
            .Select(m => (m.OrderId, m.BatchId)).ToHashSet();

        var productIds = movements.Select(m => m.ProductId).Distinct().ToList();
        var products = await db.Products.AsNoTracking().Where(p => productIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, p => new { p.Name, p.Unit }, ct);

        var items = movements.GroupBy(m => m.ProductId)
            .Where(g => products.ContainsKey(g.Key))
            .Select(g =>
            {
                decimal Sum(IEnumerable<decimal?> costs) => costs.Sum(c => c ?? 0m);
                var purchased = Sum(g.Where(m => m.Type == MovementType.Receipt).Select(m => m.Cost));
                var costOfSales = -Sum(g.Where(m => m.Type is MovementType.OrderConsumption or MovementType.OrderCancelReturn).Select(m => m.Cost));
                var expiry = -Sum(g.Where(m => m.Type == MovementType.ExpiryWriteOff).Select(m => m.Cost));
                var manual = -Sum(g.Where(m => m.Type == MovementType.ManualWriteOff).Select(m => m.Cost));
                // Продукт на грани срока, пущенный в заготовку, тоже спасён: он не ушёл в списание.
                var rescuedMoves = g.Where(m => (m.Type is MovementType.OrderConsumption or MovementType.PrepConsumption && m.Rescued)
                                                || (m.Type == MovementType.OrderCancelReturn && rescuedPairs.Contains((m.OrderId, m.BatchId))))
                    .ToList();
                var rescued = -Sum(rescuedMoves.Select(m => m.Cost));
                var rescuedQty = -rescuedMoves.Sum(m => m.QtyDelta);
                var product = products[g.Key];
                return new MoneyProductDto(g.Key, product.Name, product.Unit, purchased, costOfSales, expiry + manual, expiry, rescued, rescuedQty);
            })
            .OrderByDescending(p => p.Losses).ThenByDescending(p => p.Rescued).ThenBy(p => p.ProductName)
            .ToList();

        var costOfSalesTotal = items.Sum(i => i.CostOfSales);
        var expiryTotal = items.Sum(i => i.ExpiryLosses);
        var lossesTotal = items.Sum(i => i.Losses);
        var coverage = movements.Count == 0 ? 1m : decimal.Round((decimal)movements.Count(m => m.Cost is not null) / movements.Count, 4);

        return new MoneyReportDto(period.From, period.To, revenue, items.Sum(i => i.Purchased), costOfSalesTotal, revenue - costOfSalesTotal,
            lossesTotal, expiryTotal, lossesTotal - expiryTotal, items.Sum(i => i.Rescued), coverage, items);
    }
}
