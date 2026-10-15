namespace Mise.Domain.Costing;

/// <summary>Продукт в составе порции (базовые единицы).</summary>
public sealed record CostIngredient(Guid ProductId, decimal QtyPerPortion);

/// <summary>Пригодный остаток партии с ценой; <see cref="FefoOrder"/> — очередь списания (1 — первой).</summary>
public sealed record CostLot(Guid ProductId, decimal Qty, decimal? UnitCost, int FefoOrder);

/// <param name="PortionCost">Себестоимость порции, ₽; null — цена хотя бы одного продукта неизвестна.</param>
/// <param name="Estimated">Часть цены взята из ориентировочной цены продукта, а не из партий.</param>
public sealed record PortionCostResult(decimal? PortionCost, bool Estimated, IReadOnlyList<Guid> UnpricedProducts);

/// <param name="FoodCostShare">Доля себестоимости в цене (0,3 — 30 %); null для бесплатного блюда.</param>
public sealed record DishEconomics(decimal Margin, decimal? FoodCostShare);

/// <summary>
/// Себестоимость порции по партиям, которые уйдут в работу первыми (FEFO): так видно, сколько блюдо стоит кухне
/// прямо сейчас. Нет цены у партии или нет остатка — ориентировочная цена продукта (оценка); нет и её — неизвестно.
/// </summary>
public static class DishCosting
{
    public static PortionCostResult Cost(
        IReadOnlyList<CostIngredient> ingredients,
        IReadOnlyList<CostLot> lots,
        IReadOnlyDictionary<Guid, decimal?> referenceUnitCosts)
    {
        decimal total = 0m;
        var estimated = false;
        var unpriced = new List<Guid>();
        var byProduct = lots.Where(l => l.Qty > 0).GroupBy(l => l.ProductId).ToDictionary(g => g.Key, g => g.OrderBy(l => l.FefoOrder).ToList());

        foreach (var ingredient in ingredients)
        {
            var left = ingredient.QtyPerPortion;
            decimal cost = 0m;
            foreach (var lot in byProduct.GetValueOrDefault(ingredient.ProductId) ?? [])
            {
                if (left <= 0 || lot.UnitCost is not { } price)
                    break;
                var take = Math.Min(left, lot.Qty);
                cost += take * price;
                left -= take;
            }

            if (left > 0)
            {
                // Остаток порции не покрыт партиями с ценой — по ориентировочной цене продукта.
                if (referenceUnitCosts.GetValueOrDefault(ingredient.ProductId) is { } reference)
                {
                    cost += left * reference;
                    estimated = true;
                }
                else
                {
                    unpriced.Add(ingredient.ProductId);
                    continue;
                }
            }
            total += cost;
        }

        return unpriced.Count > 0
            ? new PortionCostResult(null, estimated, unpriced)
            : new PortionCostResult(decimal.Round(total, 2, MidpointRounding.AwayFromZero), estimated, []);
    }

    public static DishEconomics Economics(decimal price, decimal portionCost) =>
        new(price - portionCost, price > 0 ? decimal.Round(portionCost / price, 4) : null);
}
