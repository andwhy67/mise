using Mise.Application.Simulator;
using Mise.Simulator.Model;

namespace Mise.Simulator.Tests;

public class OrderGeneratorTests
{
    internal static readonly DishInfo Pasta = Dish("Паста карбонара", "Паста");
    internal static readonly DishInfo Side = Dish("Картофельное пюре", "Гарниры");
    internal static readonly DishInfo Salad = Dish("Цезарь", "Салаты");
    internal static readonly DishInfo Soup = Dish("Суп", "Супы");

    [Fact]
    public void Same_seed_gives_same_orders()
    {
        var dishes = new[] { Pasta, Side, Salad, Soup };

        var first = Enumerable.Range(0, 50).Select(_ => OrderGenerator.Next(new Random(7), dishes)).ToList();
        var a = new Random(42);
        var b = new Random(42);

        Assert.All(first, o => Assert.NotEmpty(o));
        Assert.Equal(
            Enumerable.Range(0, 50).SelectMany(_ => OrderGenerator.Next(a, dishes)),
            Enumerable.Range(0, 50).SelectMany(_ => OrderGenerator.Next(b, dishes)));
    }

    [Fact]
    public void Orders_have_one_to_four_distinct_dishes_of_one_or_two_portions()
    {
        var dishes = new[] { Pasta, Side, Salad, Soup, Dish("Пицца", "Пицца"), Dish("Омлет", "Завтраки") };
        var random = new Random(1);

        var orders = Enumerable.Range(0, 2000).Select(_ => OrderGenerator.Next(random, dishes)).ToList();

        Assert.All(orders, o =>
        {
            Assert.InRange(o.Count, 1, 4);
            Assert.Equal(o.Count, o.Select(l => l.DishId).Distinct().Count());
            Assert.All(o, l => Assert.InRange(l.Quantity, 1, 2));
        });
        var singles = orders.Count(o => o.Count == 1) / (double)orders.Count;
        Assert.InRange(singles, 0.45, 0.55);
    }

    [Fact]
    public void Hits_are_ordered_more_often_than_outsiders()
    {
        var random = new Random(3);
        var counts = new Dictionary<Guid, int> { [Pasta.Id] = 0, [Side.Id] = 0 };

        // Заказ из одного блюда — частоты пропорциональны весам (3 : 1).
        for (var i = 0; i < 20_000; i++)
            counts[Pasta.Id == PickOne(random) ? Pasta.Id : Side.Id]++;

        var ratio = counts[Pasta.Id] / (double)counts[Side.Id];
        Assert.InRange(ratio, 2.7, 3.3);
    }

    [Fact]
    public void Inactive_dishes_and_dishes_without_ingredients_are_never_ordered()
    {
        var inactive = Pasta with { Id = Guid.NewGuid(), IsActive = false };
        var empty = Salad with { Id = Guid.NewGuid(), Ingredients = [] };
        var random = new Random(5);

        var lines = Enumerable.Range(0, 500).SelectMany(_ => OrderGenerator.Next(random, [inactive, empty, Soup])).ToList();

        Assert.All(lines, l => Assert.Equal(Soup.Id, l.DishId));
        Assert.Empty(OrderGenerator.Next(random, [inactive, empty]));
    }

    private static Guid PickOne(Random random) =>
        new[] { Pasta, Side }[OrderGenerator.PickIndex(random, [OrderGenerator.WeightOf(Pasta), OrderGenerator.WeightOf(Side)])].Id;

    internal static DishInfo Dish(string name, string category) =>
        new(Guid.NewGuid(), name, category, 500m, true, [new DishIngredientInfo(Guid.NewGuid(), 100m)]);
}

public class TrafficModelTests
{
    // 09:00Z = 12:00 по Москве.
    private static readonly DateTimeOffset MoscowNoon = new(2026, 10, 12, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Lunch_and_dinner_are_peaks_and_night_is_quiet()
    {
        var night = TrafficModel.RateAt(MoscowNoon.AddHours(-9));   // 03:00
        var lunch = TrafficModel.RateAt(MoscowNoon.AddHours(1));    // 13:00
        var afternoon = TrafficModel.RateAt(MoscowNoon.AddHours(4)); // 16:00
        var dinner = TrafficModel.RateAt(MoscowNoon.AddHours(7));   // 19:00

        Assert.True(lunch > afternoon && dinner > afternoon && afternoon > night);
    }

    [Fact]
    public void Expected_integrates_hourly_rates_including_partial_hours()
    {
        Assert.Equal(TrafficModel.RateAt(MoscowNoon), TrafficModel.Expected(MoscowNoon, MoscowNoon.AddHours(1)));

        var from = MoscowNoon.AddMinutes(30);
        var to = MoscowNoon.AddMinutes(90);
        var expected = TrafficModel.RateAt(MoscowNoon) / 2 + TrafficModel.RateAt(MoscowNoon.AddHours(1)) / 2;
        Assert.Equal(expected, TrafficModel.Expected(from, to));
        Assert.Equal(0m, TrafficModel.Expected(from, from));
    }

    [Theory]
    [InlineData(0.4)]
    [InlineData(3)]
    [InlineData(55)]
    public void Poisson_sample_mean_matches_lambda(double lambda)
    {
        var random = new Random(11);
        var mean = Enumerable.Range(0, 20_000).Average(_ => Poisson.Sample(random, lambda));

        Assert.InRange(mean, lambda * 0.95, lambda * 1.05);
    }

    [Fact]
    public void Scenarios_differ_in_the_expected_direction()
    {
        var normal = ScenarioProfile.For(SimulatorScenario.Normal);

        Assert.True(ScenarioProfile.For(SimulatorScenario.RushHour).OrderRateFactor > normal.OrderRateFactor);
        Assert.False(ScenarioProfile.For(SimulatorScenario.SupplyDelay).SuppliesEnabled);
        Assert.NotNull(ScenarioProfile.For(SimulatorScenario.Spoilage).SpoilageMinutes);
        var chaos = ScenarioProfile.For(SimulatorScenario.Chaos);
        Assert.True(chaos.DuplicateProbability > 0 && chaos.BurstProbability > 0 && chaos.CancelProbability > normal.CancelProbability);
    }
}

public class SupplyPlannerTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 12, 9, 0, 0, TimeSpan.Zero);
    private static readonly ProductInfo Cream = new(Guid.NewGuid(), "Сливки", "Milliliter", 168, 2000, 1000);
    private static readonly ProductInfo Pasta = new(Guid.NewGuid(), "Спагетти", "Gram", 8760, 2000, 1000);

    [Fact]
    public void Product_below_reorder_point_is_topped_up_to_target_in_whole_packs()
    {
        var usable = new Dictionary<Guid, decimal> { [Cream.Id] = 2500, [Pasta.Id] = 3500 };

        var draft = SupplyPlanner.Plan(new Random(1), [Cream, Pasta], usable, ScenarioProfile.For(SimulatorScenario.Normal), Now, fullRestock: false);

        // Сливки: 2500 < 1,5 × 2000 → до 3 × 2000 = 6000, кратно 1000 → 4000. Спагетти: 3500 ≥ 3000 — не нужно.
        var line = Assert.Single(draft!.Items);
        Assert.Equal(Cream.Id, line.ProductId);
        Assert.Equal(4000m, line.Quantity);
        Assert.InRange(line.ShelfLifeHours!.Value, 134, 168);
        Assert.Null(line.ExpiresAt);
        Assert.Matches(@"^SUP-2026-\d{6}$", draft.Number);
    }

    [Fact]
    public void Nothing_to_order_returns_null()
    {
        var usable = new Dictionary<Guid, decimal> { [Cream.Id] = 6000, [Pasta.Id] = 6000 };

        Assert.Null(SupplyPlanner.Plan(new Random(1), [Cream, Pasta], usable, ScenarioProfile.For(SimulatorScenario.Normal), Now, false));
    }

    [Fact]
    public void Full_restock_tops_up_everything_below_target()
    {
        var usable = new Dictionary<Guid, decimal> { [Cream.Id] = 5000, [Pasta.Id] = 3500 };

        var draft = SupplyPlanner.Plan(new Random(1), [Cream, Pasta], usable, ScenarioProfile.For(SimulatorScenario.Normal), Now, fullRestock: true);

        // Самые дефицитные — первыми: спагетти заполнены на 58%, сливки — на 83%.
        Assert.Equal(new[] { 3000m, 1000m }, draft!.Items.Select(i => i.Quantity));
    }

    [Fact]
    public void Large_catalog_is_split_with_the_most_depleted_first()
    {
        var products = Enumerable.Range(0, 150).Select(i => new ProductInfo(Guid.NewGuid(), $"П{i}", "Gram", 72, 1000, 100)).ToList();
        var usable = products.Select((p, i) => (p.Id, Qty: (decimal)i * 10)).ToDictionary(x => x.Id, x => x.Qty);

        var draft = SupplyPlanner.Plan(new Random(1), products, usable, ScenarioProfile.For(SimulatorScenario.Normal), Now, fullRestock: false);

        Assert.Equal(SupplyPlanner.MaxItemsPerSupply, draft!.Items.Count);
        Assert.Equal(products[0].Id, draft.Items[0].ProductId);
        Assert.DoesNotContain(draft.Items, i => i.ProductId == products[149].Id);
    }

    [Fact]
    public void Spoilage_gives_perishables_an_explicit_short_expiry_and_keeps_dry_goods_normal()
    {
        var draft = SupplyPlanner.Plan(new Random(1), [Cream, Pasta], new Dictionary<Guid, decimal>(),
            ScenarioProfile.For(SimulatorScenario.Spoilage), Now, fullRestock: false);

        var cream = draft!.Items.Single(i => i.ProductId == Cream.Id);
        var pasta = draft.Items.Single(i => i.ProductId == Pasta.Id);
        Assert.Null(cream.ShelfLifeHours);
        Assert.InRange(cream.ExpiresAt!.Value, Now.AddMinutes(5), Now.AddMinutes(30));
        Assert.NotNull(pasta.ShelfLifeHours);
        Assert.Null(pasta.ExpiresAt);
    }
}
