using Mise.Domain.Catalog;
using Mise.Domain.Common;
using Mise.Domain.Stock;

namespace Mise.Domain.Prep;

/// <param name="Required">Сколько продукта нужно на заданное количество заготовки (базовые единицы).</param>
public sealed record PrepIngredientPlan(Guid ProductId, decimal Required, AllocationResult Allocation);

/// <param name="ExpiresAt">Срок партии заготовки: от приготовления, но не позже самого раннего срока взятых партий.</param>
/// <param name="LimitingProductId">Продукт, чей срок ограничил заготовку; null — заготовка живёт свой полный срок.</param>
/// <param name="UnitCost">Себестоимость за базовую единицу заготовки; null — цена хотя бы одной партии неизвестна.</param>
public sealed record PrepPlan(
    Guid PrepId,
    decimal Quantity,
    IReadOnlyList<PrepIngredientPlan> Ingredients,
    DateTimeOffset ExpiresAt,
    Guid? LimitingProductId,
    decimal? UnitCost)
{
    public bool IsSatisfied => Ingredients.All(i => i.Allocation.IsSatisfied);
}

/// <summary>
/// Заготовка (соус, бульон): рецепт масштабируется на нужный выход, каждый ингредиент берётся по FEFO.
/// Заготовка не может пережить продукты, из которых сделана: срок = min(сейчас + срок хранения заготовки, самый ранний срок взятых партий).
/// </summary>
public static class PrepPlanner
{
    public static class Errors
    {
        public const string NotPrep = "prep.not_prep";
        public const string InvalidQuantity = "prep.invalid_quantity";
    }

    public static PrepPlan Plan(Product prep, decimal quantity, IEnumerable<Batch> batches, DateTimeOffset now)
    {
        if (prep.PrepYield is not { } yield || prep.DefaultShelfLifeHours is not { } shelfLife)
            throw new DomainException(Errors.NotPrep, $"«{prep.Name}» — не заготовка: у продукта нет рецепта.");
        if (quantity <= 0)
            throw new DomainException(Errors.InvalidQuantity, "Количество заготовки должно быть больше нуля.");

        var all = batches.ToList();
        var byProduct = all.ToLookup(b => b.ProductId);
        var factor = quantity / yield;
        var ingredients = prep.PrepIngredients
            .OrderBy(i => i.IngredientId)
            .Select(i =>
            {
                var required = Math.Max(decimal.Round(i.Quantity * factor, 3, MidpointRounding.AwayFromZero), 0.001m);
                return new PrepIngredientPlan(i.IngredientId, required, FefoAllocator.Allocate(required, byProduct[i.IngredientId], now));
            })
            .ToList();

        var byId = all.ToDictionary(b => b.Id);
        var used = ingredients.SelectMany(i => i.Allocation.Allocations).Select(a => (Batch: byId[a.BatchId], a.Quantity)).ToList();
        var ownExpiry = now.AddHours(shelfLife);
        var earliest = used.Select(u => u.Batch).MinBy(b => b.ExpiresAt);
        var limited = earliest is not null && earliest.ExpiresAt < ownExpiry;

        decimal? total = used.Count == 0 ? null : 0m;
        foreach (var (batch, qty) in used)
            total = batch.CostOf(qty) is { } cost ? total + cost : null;

        return new PrepPlan(
            prep.Id,
            quantity,
            ingredients,
            limited ? earliest!.ExpiresAt : ownExpiry,
            limited ? earliest!.ProductId : null,
            total is { } sum ? decimal.Round(sum / quantity, 6, MidpointRounding.AwayFromZero) : null);
    }
}
