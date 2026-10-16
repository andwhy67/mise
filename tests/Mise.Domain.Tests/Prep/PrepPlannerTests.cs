using Mise.Domain.Catalog;
using Mise.Domain.Common;
using Mise.Domain.Expiry;
using Mise.Domain.Prep;
using Mise.Domain.Stock;

namespace Mise.Domain.Tests.Prep;

public class PrepPlannerTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 12, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid Cream = Guid.NewGuid();
    private static readonly Guid Mushrooms = Guid.NewGuid();

    /// <summary>Соус: на 1000 мл — 500 мл сливок и 400 г грибов, хранится 24 ч.</summary>
    private static Product Sauce()
    {
        var sauce = new Product(Guid.NewGuid(), "Соус", MeasureUnit.Milliliter) { DefaultShelfLifeHours = 24 };
        sauce.SetPrepRecipe(1000m, [(Cream, 500m), (Mushrooms, 400m)]);
        return sauce;
    }

    private static Batch NewBatch(Guid productId, decimal qty, double expiresInHours, decimal? unitCost = null) =>
        Batch.Receive(Guid.CreateVersion7(), Guid.NewGuid(), productId, Now.AddDays(-2),
            new ExpiryResolution(Now.AddHours(expiresInHours), ExpirySource.SupplyItemShelfLife), qty, Now.AddDays(-2), unitCost);

    [Fact]
    public void Scales_the_recipe_and_takes_ingredients_by_fefo()
    {
        var lateCream = NewBatch(Cream, 1000m, 72);
        var earlyCream = NewBatch(Cream, 100m, 48);
        var mushrooms = NewBatch(Mushrooms, 1000m, 96);

        var plan = PrepPlanner.Plan(Sauce(), 500m, [lateCream, earlyCream, mushrooms], Now);

        Assert.True(plan.IsSatisfied);
        var cream = plan.Ingredients.Single(i => i.ProductId == Cream);
        Assert.Equal(250m, cream.Required);
        Assert.Equal([new BatchAllocation(earlyCream.Id, 100m), new BatchAllocation(lateCream.Id, 150m)], cream.Allocation.Allocations);
        Assert.Equal(200m, plan.Ingredients.Single(i => i.ProductId == Mushrooms).Required);
    }

    [Fact]
    public void Prep_lives_its_own_shelf_life_when_ingredients_are_fresh()
    {
        var plan = PrepPlanner.Plan(Sauce(), 1000m, [NewBatch(Cream, 1000m, 72), NewBatch(Mushrooms, 1000m, 96)], Now);

        Assert.Equal(Now.AddHours(24), plan.ExpiresAt);
        Assert.Null(plan.LimitingProductId);
    }

    [Fact]
    public void Prep_never_outlives_the_earliest_ingredient_it_is_made_of()
    {
        var plan = PrepPlanner.Plan(Sauce(), 1000m, [NewBatch(Cream, 1000m, 20), NewBatch(Mushrooms, 1000m, 96)], Now);

        Assert.Equal(Now.AddHours(20), plan.ExpiresAt);
        Assert.Equal(Cream, plan.LimitingProductId);
    }

    [Fact]
    public void Reports_shortage_per_ingredient_without_allocating()
    {
        var plan = PrepPlanner.Plan(Sauce(), 1000m, [NewBatch(Cream, 300m, 72), NewBatch(Mushrooms, 1000m, 96)], Now);

        Assert.False(plan.IsSatisfied);
        var cream = plan.Ingredients.Single(i => i.ProductId == Cream);
        Assert.Equal(200m, cream.Allocation.Shortage);
        Assert.Empty(cream.Allocation.Allocations);
    }

    [Fact]
    public void Unit_cost_is_the_cost_of_ingredients_per_unit_of_prep()
    {
        var plan = PrepPlanner.Plan(Sauce(), 1000m,
            [NewBatch(Cream, 1000m, 72, unitCost: 0.5m), NewBatch(Mushrooms, 1000m, 96, unitCost: 0.35m)], Now);

        // 500 мл × 0,5 + 400 г × 0,35 = 390 ₽ на литр.
        Assert.Equal(0.39m, plan.UnitCost);
        Assert.Null(PrepPlanner.Plan(Sauce(), 1000m, [NewBatch(Cream, 1000m, 72), NewBatch(Mushrooms, 1000m, 96)], Now).UnitCost);
    }

    [Fact]
    public void Recipe_rejects_self_and_prep_ingredients_and_requires_shelf_life()
    {
        var sauce = new Product(Guid.NewGuid(), "Соус", MeasureUnit.Milliliter) { DefaultShelfLifeHours = 24 };
        Assert.Throws<DomainException>(() => sauce.SetPrepRecipe(1000m, [(sauce.Id, 100m)]));
        var broth = Guid.NewGuid();
        Assert.Throws<DomainException>(() => sauce.SetPrepRecipe(1000m, [(broth, 100m)], new HashSet<Guid> { broth }));

        var noShelfLife = new Product(Guid.NewGuid(), "Бульон", MeasureUnit.Milliliter);
        Assert.Throws<DomainException>(() => noShelfLife.SetPrepRecipe(1000m, [(Cream, 100m)]));

        Assert.Throws<DomainException>(() => PrepPlanner.Plan(noShelfLife, 100m, [], Now));
    }
}
