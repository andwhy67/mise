using Mise.Domain.Catalog;
using Mise.Domain.Orders;

namespace Mise.Domain.Menu;

public enum DishAvailabilityStatus
{
    Available,
    /// <summary>Не хватает ингредиентов хотя бы на одну порцию.</summary>
    StopShortage,
    /// <summary>Блюдо выключено вручную.</summary>
    StopManual,
}

public sealed record IngredientAvailability(Guid ProductId, decimal QtyPerPortion, decimal Usable, int Portions);

/// <summary>Блюдо в стопе, но его можно подать с изменениями (убрать гарнир, заменить продукт) — столько порций.</summary>
public sealed record DishAlternative(int Portions, IReadOnlyList<ItemModification> Changes);

/// <summary>Чем можно заменить продукт: заменитель и сколько его на 1 единицу (правило справочника).</summary>
public sealed record SubstituteRule(Guid SubstituteId, decimal Ratio);

public sealed record DishAvailability(
    Guid DishId,
    int MaxPortions,
    Guid? LimitingProductId,
    DishAvailabilityStatus Status,
    IReadOnlyList<IngredientAvailability> Ingredients)
{
    public bool InStopList => Status != DishAvailabilityStatus.Available;
}

/// <summary>
/// Доступность блюда по пригодному остатку (usable — без просроченного):
/// maxPortions = min по ингредиентам floor(usable / q); в стопе ⇔ maxPortions = 0 или блюдо выключено вручную.
/// </summary>
public static class MenuAvailability
{
    public static DishAvailability Calculate(Dish dish, IReadOnlyDictionary<Guid, decimal> usableByProduct)
    {
        var ingredients = dish.Ingredients
            .Select(i =>
            {
                var usable = usableByProduct.GetValueOrDefault(i.ProductId);
                return new IngredientAvailability(i.ProductId, i.Quantity, usable, Portions(usable, i.Quantity));
            })
            .OrderBy(i => i.Portions).ThenBy(i => i.ProductId)
            .ToList();

        var limiting = ingredients.FirstOrDefault();
        var maxPortions = limiting?.Portions ?? 0;
        var status = !dish.IsActive ? DishAvailabilityStatus.StopManual
            : maxPortions == 0 ? DishAvailabilityStatus.StopShortage
            : DishAvailabilityStatus.Available;

        return new DishAvailability(dish.Id, maxPortions, limiting?.ProductId, status, ingredients);
    }

    private static int Portions(decimal usable, decimal perPortion)
    {
        if (perPortion <= 0)
            return int.MaxValue;
        var portions = decimal.Floor(usable / perPortion);
        return portions >= int.MaxValue ? int.MaxValue : (int)portions;
    }

    /// <summary>
    /// Блюдо в стопе из-за нехватки — можно ли подать его с изменениями: каждый недостающий продукт заменить
    /// (из правил справочника, если заменителя хватает) или убрать (если ингредиент «можно убрать»). Замена — в приоритете:
    /// блюдо ближе к рецепту. Стоп-лист остаётся честным: это не «блюдо доступно», а «можно предложить вот так».
    /// </summary>
    public static DishAlternative? Alternative(
        Dish dish,
        IReadOnlyDictionary<Guid, decimal> usableByProduct,
        IReadOnlyDictionary<Guid, IReadOnlyList<SubstituteRule>> substitutes)
    {
        if (!dish.IsActive)
            return null;
        decimal Usable(Guid id) => usableByProduct.GetValueOrDefault(id);
        var shortages = dish.Ingredients.Where(i => i.Quantity > 0 && Usable(i.ProductId) < i.Quantity).ToList();
        if (shortages.Count == 0)
            return null;

        var changes = new List<ItemModification>();
        foreach (var ingredient in shortages)
        {
            var best = (substitutes.GetValueOrDefault(ingredient.ProductId) ?? [])
                .Where(r => shortages.All(s => s.ProductId != r.SubstituteId))
                .Select(r => new ItemModification(ingredient.ProductId, ModificationKind.Replace, r.SubstituteId, r.Ratio))
                .Select(change => (Change: change, Portions: PortionsWith(dish, [.. changes, change], Usable, shortages)))
                .Where(x => x.Portions > 0)
                .OrderByDescending(x => x.Portions).ThenBy(x => x.Change.SubstituteId)
                .FirstOrDefault();
            if (best.Change is not null)
                changes.Add(best.Change);
            else if (ingredient.Removable)
                changes.Add(new ItemModification(ingredient.ProductId, ModificationKind.Remove));
            else
                return null;
        }

        var portions = PortionsWith(dish, changes, Usable, shortages);
        return portions > 0 ? new DishAlternative(portions, changes) : null;
    }

    /// <summary>Порций с изменениями: потребность на порцию по продуктам (заменитель может уже быть в составе — суммируем).</summary>
    private static int PortionsWith(Dish dish, IReadOnlyList<ItemModification> changes, Func<Guid, decimal> usable, IReadOnlyList<DishIngredient> shortages)
    {
        var perPortion = new Dictionary<Guid, decimal>();
        foreach (var ingredient in dish.Ingredients)
        {
            var change = changes.FirstOrDefault(c => c.ProductId == ingredient.ProductId);
            if (change is { Kind: ModificationKind.Remove })
                continue;
            if (change is { Kind: ModificationKind.Replace, SubstituteId: { } substitute })
                perPortion[substitute] = perPortion.GetValueOrDefault(substitute) + ingredient.Quantity * change.Ratio;
            else if (shortages.All(s => s.ProductId != ingredient.ProductId))
                perPortion[ingredient.ProductId] = perPortion.GetValueOrDefault(ingredient.ProductId) + ingredient.Quantity;
        }
        return perPortion.Count == 0 ? 0 : perPortion.Min(p => Portions(usable(p.Key), p.Value));
    }
}
