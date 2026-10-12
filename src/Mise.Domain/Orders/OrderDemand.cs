using Mise.Domain.Catalog;

namespace Mise.Domain.Orders;

/// <summary>
/// Суммарная потребность заказа по продуктам: Σ (кол-во в составе × порции) по всем блюдам.
/// Проверка наличия идёт по этой сумме, а не по каждому блюду отдельно (CLAUDE.md, правило 4).
/// </summary>
public static class OrderDemand
{
    public static IReadOnlyDictionary<Guid, decimal> Aggregate(IEnumerable<(Dish Dish, int Portions)> lines)
    {
        var demand = new Dictionary<Guid, decimal>();
        foreach (var (dish, portions) in lines)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(portions);
            foreach (var ingredient in dish.Ingredients)
                demand[ingredient.ProductId] = demand.GetValueOrDefault(ingredient.ProductId) + ingredient.Quantity * portions;
        }
        return demand;
    }
}
