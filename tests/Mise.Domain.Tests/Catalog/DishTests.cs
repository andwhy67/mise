using Mise.Domain.Catalog;

namespace Mise.Domain.Tests.Catalog;

public class DishTests
{
    private static readonly Guid Cream = Guid.NewGuid();
    private static readonly Guid Pasta = Guid.NewGuid();
    private static readonly Guid Bacon = Guid.NewGuid();

    [Fact]
    public void SetIngredients_replaces_composition_keeping_existing_rows()
    {
        var dish = new Dish(Guid.NewGuid(), "Карбонара");
        dish.SetIngredients([(Cream, 50m), (Pasta, 120m)]);
        var pastaRow = dish.Ingredients.Single(i => i.ProductId == Pasta);

        dish.SetIngredients([(Pasta, 100m), (Bacon, 60m)]);

        Assert.Equal(2, dish.Ingredients.Count);
        Assert.DoesNotContain(dish.Ingredients, i => i.ProductId == Cream);
        Assert.Same(pastaRow, dish.Ingredients.Single(i => i.ProductId == Pasta));
        Assert.Equal(100m, pastaRow.Quantity);
        Assert.Equal(60m, dish.Ingredients.Single(i => i.ProductId == Bacon).Quantity);
        Assert.All(dish.Ingredients, i => Assert.Equal(dish.Id, i.DishId));
    }
}
