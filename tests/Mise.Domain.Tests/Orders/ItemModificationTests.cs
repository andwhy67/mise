using Mise.Domain.Catalog;
using Mise.Domain.Common;
using Mise.Domain.Menu;
using Mise.Domain.Orders;

namespace Mise.Domain.Tests.Orders;

/// <summary>«Без лимона», «шпинат → руккола»: изменения строки заказа и «можно приготовить с изменениями».</summary>
public class ItemModificationTests
{
    private static readonly Guid Salmon = Guid.NewGuid();
    private static readonly Guid Spinach = Guid.NewGuid();
    private static readonly Guid Arugula = Guid.NewGuid();
    private static readonly Guid Lemon = Guid.NewGuid();

    /// <summary>Лосось на гриле: лосось 180 г, шпинат 80 г, лимон 1 шт (лимон можно убрать).</summary>
    private static Dish GrilledSalmon()
    {
        var dish = new Dish(Guid.NewGuid(), "Лосось на гриле");
        dish.SetIngredients([(Salmon, 180m, false), (Spinach, 80m, false), (Lemon, 1m, true)]);
        return dish;
    }

    private static readonly Dictionary<Guid, IReadOnlyList<SubstituteRule>> Rules = new() { [Spinach] = [new SubstituteRule(Arugula, 1m)] };

    [Fact]
    public void Removed_ingredient_is_not_demanded_and_replaced_one_goes_to_the_substitute()
    {
        var demand = OrderDemand.Aggregate(
            [new DemandLine(GrilledSalmon(), 2, [new ItemModification(Lemon, ModificationKind.Remove), new ItemModification(Spinach, ModificationKind.Replace, Arugula, 1.5m)])],
            []);

        Assert.False(demand.ContainsKey(Lemon));
        Assert.False(demand.ContainsKey(Spinach));
        Assert.Equal(240m, demand[Arugula]);
        Assert.Equal(360m, demand[Salmon]);
    }

    [Fact]
    public void Only_removable_ingredients_can_be_removed()
    {
        var error = Assert.Throws<DomainException>(() =>
            OrderDemand.Aggregate([new DemandLine(GrilledSalmon(), 1, [new ItemModification(Salmon, ModificationKind.Remove)])], []));
        Assert.Equal(OrderDemand.Errors.ModificationNotAllowed, error.Code);
    }

    [Fact]
    public void Dish_without_lemons_can_still_be_served_without_lemon()
    {
        var usable = new Dictionary<Guid, decimal> { [Salmon] = 1800m, [Spinach] = 400m, [Lemon] = 0m };

        var alternative = MenuAvailability.Alternative(GrilledSalmon(), usable, Rules);

        var change = Assert.Single(alternative!.Changes);
        Assert.Equal(new ItemModification(Lemon, ModificationKind.Remove), change);
        Assert.Equal(5, alternative.Portions); // шпината — на 5 порций
    }

    [Fact]
    public void Missing_product_with_a_substitute_is_replaced_rather_than_removed()
    {
        var usable = new Dictionary<Guid, decimal> { [Salmon] = 1800m, [Spinach] = 0m, [Arugula] = 300m, [Lemon] = 10m };

        var alternative = MenuAvailability.Alternative(GrilledSalmon(), usable, Rules);

        Assert.Equal(new ItemModification(Spinach, ModificationKind.Replace, Arugula, 1m), Assert.Single(alternative!.Changes));
        Assert.Equal(3, alternative.Portions);
    }

    [Fact]
    public void Without_a_substitute_and_not_removable_the_dish_stays_in_the_stop_list()
    {
        var usable = new Dictionary<Guid, decimal> { [Salmon] = 0m, [Spinach] = 400m, [Lemon] = 10m };

        Assert.Null(MenuAvailability.Alternative(GrilledSalmon(), usable, Rules));
    }
}
