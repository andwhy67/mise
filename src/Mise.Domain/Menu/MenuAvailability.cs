using Mise.Domain.Catalog;

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
}
