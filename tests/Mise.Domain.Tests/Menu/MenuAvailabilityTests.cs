using Mise.Domain.Catalog;
using Mise.Domain.Menu;

namespace Mise.Domain.Tests.Menu;

public class MenuAvailabilityTests
{
    private static readonly Guid Cream = Guid.NewGuid();
    private static readonly Guid Pasta = Guid.NewGuid();

    private static Dish Carbonara(bool active = true)
    {
        var dish = new Dish(Guid.NewGuid(), "Карбонара") { IsActive = active };
        dish.SetIngredients([(Pasta, 120m), (Cream, 50m)]);
        return dish;
    }

    [Fact]
    public void Max_portions_is_the_floor_of_the_scarcest_ingredient()
    {
        var usable = new Dictionary<Guid, decimal> { [Pasta] = 1000m, [Cream] = 175m };

        var result = MenuAvailability.Calculate(Carbonara(), usable);

        Assert.Equal(3, result.MaxPortions);
        Assert.Equal(Cream, result.LimitingProductId);
        Assert.Equal(DishAvailabilityStatus.Available, result.Status);
        Assert.False(result.InStopList);
    }

    [Fact]
    public void Dish_goes_to_stop_list_when_any_ingredient_is_short_for_one_portion()
    {
        var usable = new Dictionary<Guid, decimal> { [Pasta] = 1000m, [Cream] = 49.999m };

        var result = MenuAvailability.Calculate(Carbonara(), usable);

        Assert.Equal(0, result.MaxPortions);
        Assert.Equal(DishAvailabilityStatus.StopShortage, result.Status);
        Assert.True(result.InStopList);
        Assert.Equal(Cream, result.LimitingProductId);
    }

    [Fact]
    public void Missing_product_counts_as_zero()
    {
        var result = MenuAvailability.Calculate(Carbonara(), new Dictionary<Guid, decimal> { [Pasta] = 1000m });

        Assert.Equal(0, result.MaxPortions);
        Assert.Equal(Cream, result.LimitingProductId);
    }

    [Fact]
    public void Manually_disabled_dish_is_in_stop_list_even_with_stock()
    {
        var usable = new Dictionary<Guid, decimal> { [Pasta] = 1000m, [Cream] = 1000m };

        var result = MenuAvailability.Calculate(Carbonara(active: false), usable);

        Assert.Equal(DishAvailabilityStatus.StopManual, result.Status);
        Assert.True(result.InStopList);
        Assert.Equal(8, result.MaxPortions);
    }

    [Fact]
    public void Ingredient_breakdown_is_reported()
    {
        var usable = new Dictionary<Guid, decimal> { [Pasta] = 600m, [Cream] = 175m };

        var result = MenuAvailability.Calculate(Carbonara(), usable);

        var cream = result.Ingredients.Single(i => i.ProductId == Cream);
        Assert.Equal(50m, cream.QtyPerPortion);
        Assert.Equal(175m, cream.Usable);
        Assert.Equal(3, cream.Portions);
        Assert.Equal(5, result.Ingredients.Single(i => i.ProductId == Pasta).Portions);
    }
}
