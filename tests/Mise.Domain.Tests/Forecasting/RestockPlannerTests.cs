using Mise.Domain.Forecasting;

namespace Mise.Domain.Tests.Forecasting;

public sealed class RestockPlannerTests
{
    private static readonly Guid Salmon = Guid.NewGuid();
    private static readonly Guid Cream = Guid.NewGuid();
    private static readonly Guid Pasta = Guid.NewGuid();
    private static readonly Guid SalmonSteak = Guid.NewGuid();
    private static readonly Guid SalmonPasta = Guid.NewGuid();

    private static readonly TimeSpan Day = TimeSpan.FromHours(24);

    private static RestockDish Steak(decimal perDay = 0m) =>
        new(SalmonSteak, [new ForecastIngredient(Salmon, 200m)], perDay);

    private static RestockDish SalmonPastaDish(decimal perDay = 0m) =>
        new(SalmonPasta, [new ForecastIngredient(Salmon, 100m), new ForecastIngredient(Cream, 50m), new ForecastIngredient(Pasta, 120m)], perDay);

    private static Dictionary<Guid, RestockProduct> Stock(decimal salmon, decimal cream = 10_000m, decimal pasta = 10_000m) => new()
    {
        [Salmon] = new RestockProduct(Salmon, salmon, PackSize: 1000m),
        [Cream] = new RestockProduct(Cream, cream, PackSize: 1000m),
        [Pasta] = new RestockProduct(Pasta, pasta, PackSize: 500m),
    };

    [Fact]
    public void Without_sales_history_target_is_the_minimum_portions()
    {
        var plan = RestockPlanner.Plan([Steak()], Stock(salmon: 150m), Day, minPortions: 5);

        var dish = Assert.Single(plan.Dishes);
        Assert.Equal(5, dish.TargetPortions);
        var line = Assert.Single(dish.Lines);
        Assert.Equal(1000m, line.Needed);   // 5 × 200 г
        Assert.Equal(850m, line.Missing);   // есть 150 г
        var buy = Assert.Single(plan.Purchases);
        Assert.Equal(850m, buy.Missing);
        Assert.Equal(1000m, buy.ToBuy);     // до упаковки 1 кг
    }

    [Fact]
    public void Target_follows_expected_demand_over_the_horizon()
    {
        // 12 порций в день, горизонт 24 ч → 12 порций (больше минимума 5).
        var plan = RestockPlanner.Plan([Steak(perDay: 11.2m)], Stock(salmon: 0m), Day, minPortions: 5);

        Assert.Equal(12, plan.Dishes[0].TargetPortions);
        Assert.Equal(2400m, plan.Dishes[0].Lines[0].Missing);
        Assert.Equal(3000m, plan.Purchases[0].ToBuy);
    }

    [Fact]
    public void Shared_product_is_bought_once_for_all_stopped_dishes()
    {
        // Стейк 5 × 200 + паста 5 × 100 = 1500 г лосося; есть 100 г → не хватает 1400, покупаем 2 кг.
        var plan = RestockPlanner.Plan([Steak(), SalmonPastaDish()], Stock(salmon: 100m), Day, minPortions: 5);

        var salmon = Assert.Single(plan.Purchases);
        Assert.Equal(Salmon, salmon.ProductId);
        Assert.Equal(1400m, salmon.Missing);
        Assert.Equal(2000m, salmon.ToBuy);
        Assert.Equal([SalmonSteak, SalmonPasta], salmon.Dishes);
    }

    [Fact]
    public void Ingredients_in_stock_are_not_listed()
    {
        var plan = RestockPlanner.Plan([SalmonPastaDish()], Stock(salmon: 0m), Day, minPortions: 5);

        var line = Assert.Single(plan.Dishes[0].Lines);
        Assert.Equal(Salmon, line.ProductId);
        Assert.DoesNotContain(plan.Purchases, p => p.ProductId == Cream || p.ProductId == Pasta);
    }

    [Fact]
    public void Each_dish_alone_fits_but_together_they_do_not()
    {
        // Отдельно каждому хватает (по 500 г на 5 порций), вместе нужно 1000 г при 800 г — докупить 200, т. е. упаковку.
        var steakLight = new RestockDish(SalmonSteak, [new ForecastIngredient(Salmon, 100m), new ForecastIngredient(Pasta, 1000m)], 0m);
        var plan = RestockPlanner.Plan([steakLight, SalmonPastaDish()], Stock(salmon: 800m, pasta: 0m), Day, minPortions: 5);

        var salmon = Assert.Single(plan.Purchases, p => p.ProductId == Salmon);
        Assert.Equal(200m, salmon.Missing);
        Assert.Equal(1000m, salmon.ToBuy);
        Assert.DoesNotContain(plan.Dishes[0].Lines, l => l.ProductId == Salmon);
    }

    [Fact]
    public void Unknown_product_has_pack_of_one()
    {
        var plan = RestockPlanner.Plan([Steak()], new Dictionary<Guid, RestockProduct>(), Day, minPortions: 2);

        Assert.Equal(400m, plan.Purchases[0].ToBuy);
    }
}
