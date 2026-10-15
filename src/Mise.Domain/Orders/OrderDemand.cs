using Mise.Domain.Catalog;
using Mise.Domain.Common;

namespace Mise.Domain.Orders;

/// <summary>Замена в заказе: весь <see cref="ProductId"/> заказа берётся из <see cref="SubstituteId"/> × <see cref="Ratio"/>.</summary>
public sealed record Substitution(Guid ProductId, Guid SubstituteId, decimal Ratio);

/// <summary>
/// Суммарная потребность заказа по продуктам: Σ (кол-во в составе × порции) по всем блюдам.
/// Проверка наличия идёт по этой сумме, а не по каждому блюду отдельно (CLAUDE.md, правило 4).
/// Замены (с согласия повара) переносят потребность на заменитель до проверки — она остаётся одной суммарной.
/// </summary>
public static class OrderDemand
{
    public static class Errors
    {
        public const string SubstitutionNotUsed = "order.substitution_not_used";
        public const string SubstitutionConflict = "order.substitution_conflict";
    }

    public static IReadOnlyDictionary<Guid, decimal> Aggregate(IEnumerable<(Dish Dish, int Portions)> lines) => Aggregate(lines, []);

    public static IReadOnlyDictionary<Guid, decimal> Aggregate(IEnumerable<(Dish Dish, int Portions)> lines, IReadOnlyCollection<Substitution> substitutions)
    {
        var demand = new Dictionary<Guid, decimal>();
        foreach (var (dish, portions) in lines)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(portions);
            foreach (var ingredient in dish.Ingredients)
                demand[ingredient.ProductId] = demand.GetValueOrDefault(ingredient.ProductId) + ingredient.Quantity * portions;
        }
        if (substitutions.Count == 0)
            return demand;

        var replaced = substitutions.Select(s => s.ProductId).ToList();
        if (replaced.Count != replaced.Distinct().Count() || substitutions.Any(s => replaced.Contains(s.SubstituteId)))
            throw new DomainException(Errors.SubstitutionConflict, "Продукт заменяется один раз, и заменитель сам не может быть заменён.");

        foreach (var substitution in substitutions)
        {
            if (!demand.Remove(substitution.ProductId, out var qty))
                throw new DomainException(Errors.SubstitutionNotUsed, "Замена указана для продукта, которого нет в блюдах заказа.");
            demand[substitution.SubstituteId] = demand.GetValueOrDefault(substitution.SubstituteId) + qty * substitution.Ratio;
        }
        return demand;
    }
}
