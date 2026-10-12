using Mise.Domain.Catalog;
using Mise.Domain.Orders;

namespace Mise.Domain.Tests.Orders;

public class OrderDemandTests
{
    private static readonly Guid Cream = Guid.NewGuid();
    private static readonly Guid Pasta = Guid.NewGuid();
    private static readonly Guid Salmon = Guid.NewGuid();

    private static Dish NewDish(params (Guid, decimal)[] ingredients)
    {
        var dish = new Dish(Guid.NewGuid(), "Блюдо");
        dish.SetIngredients(ingredients);
        return dish;
    }

    [Fact]
    public void Shared_ingredient_is_summed_across_dishes_and_portions()
    {
        var carbonara = NewDish((Pasta, 120m), (Cream, 50m));
        var salmonPasta = NewDish((Pasta, 120m), (Salmon, 100m), (Cream, 100m));

        var demand = OrderDemand.Aggregate([(carbonara, 2), (salmonPasta, 1)]);

        Assert.Equal(360m, demand[Pasta]);
        Assert.Equal(200m, demand[Cream]);
        Assert.Equal(100m, demand[Salmon]);
        Assert.Equal(3, demand.Count);
    }

    [Fact]
    public void Each_dish_fits_alone_but_the_order_together_does_not()
    {
        // Сливок 150: карбонаре нужно 50, пасте с лососем 100 — каждая по отдельности проходит,
        // но заказ «2 карбонары + 1 паста с лососем» требует 200 → нехватка (CLAUDE.md, правило 4).
        var carbonara = NewDish((Cream, 50m));
        var salmonPasta = NewDish((Cream, 100m));
        const decimal usableCream = 150m;

        Assert.True(OrderDemand.Aggregate([(carbonara, 2)])[Cream] <= usableCream);
        Assert.True(OrderDemand.Aggregate([(salmonPasta, 1)])[Cream] <= usableCream);
        Assert.True(OrderDemand.Aggregate([(carbonara, 2), (salmonPasta, 1)])[Cream] > usableCream);
    }

    [Fact]
    public void Same_dish_listed_twice_is_merged()
    {
        var dish = NewDish((Cream, 30m));

        var demand = OrderDemand.Aggregate([(dish, 1), (dish, 2)]);

        Assert.Equal(90m, demand[Cream]);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Non_positive_portions_are_rejected(int portions)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => OrderDemand.Aggregate([(NewDish((Cream, 1m)), portions)]));
    }
}
