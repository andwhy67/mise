using Mise.Domain.Costing;

namespace Mise.Domain.Tests.Costing;

/// <summary>
/// Себестоимость порции — по партиям, которые уйдут в работу первыми (FEFO), а не по средней цене:
/// так видно, сколько блюдо стоит кухне прямо сейчас.
/// </summary>
public sealed class DishCostingTests
{
    private static readonly Guid Salmon = Guid.NewGuid(), Cream = Guid.NewGuid(), Salt = Guid.NewGuid();

    private static CostLot Lot(Guid product, decimal qty, decimal? unitCost, int order) => new(product, qty, unitCost, order);

    [Fact]
    public void Portion_costs_by_the_first_batch_in_fefo_order()
    {
        var result = DishCosting.Cost(
            [new CostIngredient(Salmon, 100m)],
            [Lot(Salmon, 1000m, 1.0m, order: 2), Lot(Salmon, 1000m, 0.9m, order: 1)],
            new Dictionary<Guid, decimal?>());

        Assert.Equal(90m, result.PortionCost);
        Assert.False(result.Estimated);
    }

    [Fact]
    public void Portion_that_spans_two_batches_takes_both_prices()
    {
        var result = DishCosting.Cost(
            [new CostIngredient(Salmon, 100m)],
            [Lot(Salmon, 40m, 0.9m, order: 1), Lot(Salmon, 1000m, 1.0m, order: 2)],
            new Dictionary<Guid, decimal?>());

        Assert.Equal(96m, result.PortionCost); // 40 × 0,9 + 60 × 1,0
    }

    [Fact]
    public void Unpriced_or_missing_stock_falls_back_to_the_reference_price_and_is_marked_as_estimate()
    {
        var result = DishCosting.Cost(
            [new CostIngredient(Salmon, 100m), new CostIngredient(Cream, 50m)],
            [Lot(Salmon, 1000m, null, order: 1)],
            new Dictionary<Guid, decimal?> { [Salmon] = 0.95m, [Cream] = 0.4m });

        Assert.Equal(115m, result.PortionCost); // 100 × 0,95 + 50 × 0,4
        Assert.True(result.Estimated);
    }

    [Fact]
    public void Unknown_price_anywhere_means_unknown_portion_cost()
    {
        var result = DishCosting.Cost(
            [new CostIngredient(Salmon, 100m), new CostIngredient(Salt, 5m)],
            [Lot(Salmon, 1000m, 0.9m, order: 1)],
            new Dictionary<Guid, decimal?>());

        Assert.Null(result.PortionCost);
        Assert.Equal([Salt], result.UnpricedProducts);
    }

    [Theory]
    [InlineData(450, 90, 360, 0.2)]
    [InlineData(90, 90, 0, 1.0)]
    public void Margin_and_food_cost_share(decimal price, decimal cost, decimal margin, decimal share)
    {
        var economics = DishCosting.Economics(price, cost);

        Assert.Equal(margin, economics.Margin);
        Assert.Equal(share, economics.FoodCostShare);
    }

    [Fact]
    public void Free_dish_has_no_food_cost_share()
    {
        Assert.Null(DishCosting.Economics(0m, 10m).FoodCostShare);
    }
}
