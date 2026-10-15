using Mise.Domain.Catalog;
using Mise.Domain.Common;
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

    [Fact]
    public void Substitution_moves_the_demand_to_the_substitute_with_its_ratio()
    {
        var milk = Guid.NewGuid();
        var carbonara = NewDish((Pasta, 120m), (Cream, 50m));
        var salmonPasta = NewDish((Salmon, 100m), (Cream, 100m), (milk, 20m));

        // Сливки кончились — повар согласился на молоко 1,5 : 1 для всего заказа.
        var demand = OrderDemand.Aggregate([(carbonara, 2), (salmonPasta, 1)], [new Substitution(Cream, milk, 1.5m)]);

        Assert.False(demand.ContainsKey(Cream));
        Assert.Equal(20m + 200m * 1.5m, demand[milk]); // своё молоко + замена сливок, всё — в одну суммарную проверку
        Assert.Equal(240m, demand[Pasta]);
    }

    [Fact]
    public void Substitution_of_a_product_not_in_the_order_is_rejected()
    {
        var carbonara = NewDish((Pasta, 120m));

        var error = Assert.Throws<DomainException>(() => OrderDemand.Aggregate([(carbonara, 1)], [new Substitution(Cream, Salmon, 1m)]));
        Assert.Equal(OrderDemand.Errors.SubstitutionNotUsed, error.Code);
    }

    [Fact]
    public void Product_can_be_substituted_only_once_and_not_by_a_substituted_one()
    {
        var milk = Guid.NewGuid();
        var dish = NewDish((Cream, 50m), (milk, 10m));

        Assert.Throws<DomainException>(() => OrderDemand.Aggregate([(dish, 1)], [new Substitution(Cream, milk, 1m), new Substitution(Cream, Salmon, 1m)]));
        Assert.Throws<DomainException>(() => OrderDemand.Aggregate([(dish, 1)], [new Substitution(Cream, milk, 1m), new Substitution(milk, Salmon, 1m)]));
    }
}
