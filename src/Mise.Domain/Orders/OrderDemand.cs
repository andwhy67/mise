using Mise.Domain.Catalog;
using Mise.Domain.Common;

namespace Mise.Domain.Orders;

/// <summary>Замена в заказе: весь <see cref="ProductId"/> заказа берётся из <see cref="SubstituteId"/> × <see cref="Ratio"/>.</summary>
public sealed record Substitution(Guid ProductId, Guid SubstituteId, decimal Ratio);

public enum ModificationKind
{
    /// <summary>«Без этого»: продукт не кладётся и не списывается (только для ингредиентов «можно убрать»).</summary>
    Remove,
    /// <summary>Вместо продукта — заменитель × коэффициент (правило из справочника).</summary>
    Replace,
}

/// <summary>Изменение одной строки заказа: «без лимона», «шпинат → руккола».</summary>
public sealed record ItemModification(Guid ProductId, ModificationKind Kind, Guid? SubstituteId = null, decimal Ratio = 1m);

/// <summary>Строка заказа для расчёта потребности: блюдо × порции со своими изменениями.</summary>
public sealed record DemandLine(Dish Dish, int Portions, IReadOnlyList<ItemModification> Modifications);

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
        public const string ModificationNotAllowed = "order.modification_not_allowed";
    }

    public static IReadOnlyDictionary<Guid, decimal> Aggregate(IEnumerable<(Dish Dish, int Portions)> lines) => Aggregate(lines, []);

    public static IReadOnlyDictionary<Guid, decimal> Aggregate(IEnumerable<(Dish Dish, int Portions)> lines, IReadOnlyCollection<Substitution> substitutions) =>
        Aggregate(lines.Select(l => new DemandLine(l.Dish, l.Portions, [])), substitutions);

    /// <summary>
    /// Потребность с изменениями строк: убранное не считается, заменённое — заменителем × коэффициент;
    /// затем замены на весь заказ (по согласию повара после отказа). Проверка остаётся одной суммарной.
    /// </summary>
    public static IReadOnlyDictionary<Guid, decimal> Aggregate(IEnumerable<DemandLine> lines, IReadOnlyCollection<Substitution> substitutions)
    {
        var demand = new Dictionary<Guid, decimal>();
        foreach (var line in lines)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(line.Portions);
            var changes = Validate(line);
            foreach (var ingredient in line.Dish.Ingredients)
            {
                var qty = ingredient.Quantity * line.Portions;
                switch (changes.GetValueOrDefault(ingredient.ProductId))
                {
                    case { Kind: ModificationKind.Remove }:
                        continue;
                    case { Kind: ModificationKind.Replace, SubstituteId: { } substitute, Ratio: var ratio }:
                        demand[substitute] = demand.GetValueOrDefault(substitute) + qty * ratio;
                        break;
                    default:
                        demand[ingredient.ProductId] = demand.GetValueOrDefault(ingredient.ProductId) + qty;
                        break;
                }
            }
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

    private static Dictionary<Guid, ItemModification> Validate(DemandLine line)
    {
        var changes = new Dictionary<Guid, ItemModification>();
        foreach (var change in line.Modifications)
        {
            var ingredient = line.Dish.Ingredients.FirstOrDefault(i => i.ProductId == change.ProductId);
            var valid = ingredient is not null && !changes.ContainsKey(change.ProductId) && change.Kind switch
            {
                ModificationKind.Remove => ingredient.Removable,
                ModificationKind.Replace => change.SubstituteId is { } s && s != change.ProductId && change.Ratio > 0,
                _ => false,
            };
            if (!valid)
                throw new DomainException(Errors.ModificationNotAllowed,
                    $"«{line.Dish.Name}»: этот ингредиент нельзя убрать или заменить так.");
            changes[change.ProductId] = change;
        }
        return changes;
    }
}
