using Mise.Domain.Common;
using Mise.Domain.HotOffers;

namespace Mise.Domain.Tests.HotOffers;

/// <summary>
/// «Что продавать» — всё меню, а не только топ-5: блюда, которые спасают продукты; блюда, которые разгружают
/// склад (скоропортящегося много относительно спроса); остальное — по тому, сколько можно приготовить.
/// </summary>
public sealed class MenuRankingTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 15, 9, 0, 0, TimeSpan.Zero);
    private static readonly MenuRankingParameters Params = new(OverstockDays: 4m, PerishableMaxDays: 30m);

    private static readonly Guid Salmon = Guid.NewGuid(), Salt = Guid.NewGuid(), Rice = Guid.NewGuid(), Herbs = Guid.NewGuid();

    private static HotOfferInput Input(params HotOfferDish[] dishes) => new(
        [
            new HotOfferProduct(Salmon, "Лосось", MeasureUnit.Gram, 72, 500m, DailyConsumption: 1000m),
            new HotOfferProduct(Salt, "Соль", MeasureUnit.Gram, 17520, 100m, DailyConsumption: 50m),
            new HotOfferProduct(Rice, "Рис", MeasureUnit.Gram, 240, 1000m, DailyConsumption: 0m),
            new HotOfferProduct(Herbs, "Зелень", MeasureUnit.Gram, 96, 100m, DailyConsumption: 400m),
        ],
        [
            new HotOfferBatch(Guid.NewGuid(), Salmon, Now.AddDays(-1), Now.AddDays(2), 10_000m), // 10 дней спроса
            new HotOfferBatch(Guid.NewGuid(), Salt, Now.AddDays(-10), Now.AddDays(700), 10_000m), // 200 дней, но не портится
            new HotOfferBatch(Guid.NewGuid(), Rice, Now.AddDays(-1), Now.AddDays(9), 5_000m), // продаж нет
            new HotOfferBatch(Guid.NewGuid(), Herbs, Now.AddDays(-1), Now.AddDays(3), 800m), // 2 дня — обычный запас
        ],
        dishes,
        Now);

    private static HotOfferDish Dish(string name, int portions, params Guid[] products) =>
        new(Guid.NewGuid(), name, portions, products.Select(p => new HotOfferIngredient(p, 100m)).ToList());

    private static HotOffer Offer(HotOfferDish dish, decimal score) => new(dish.Id, dish.Name, dish.MaxPortions, score, 1m, []);

    [Fact]
    public void Dishes_that_save_products_come_first_by_score()
    {
        var a = Dish("Паста", 10, Herbs);
        var b = Dish("Салат", 10, Herbs);

        var ranked = MenuRanking.Rank(Input(a, b), [Offer(a, 30m), Offer(b, 60m)], HotOfferParameters.Default, Params);

        Assert.Equal([b.Id, a.Id], ranked.Where(r => r.Group == MenuGroup.Saves).Select(r => r.DishId));
    }

    [Fact]
    public void Perishable_product_with_many_days_of_stock_makes_the_dish_an_overstock_pick()
    {
        var salmon = Dish("Лосось на гриле", 20, Salmon, Salt);

        var ranked = Assert.Single(MenuRanking.Rank(Input(salmon), [], HotOfferParameters.Default, Params));

        Assert.Equal(MenuGroup.Overstock, ranked.Group);
        Assert.Equal(Salmon, ranked.Cover!.ProductId);
        Assert.Equal(10m, ranked.Cover.CoverDays);
    }

    [Fact]
    public void Non_perishable_or_unsold_stock_is_not_a_reason_to_push_a_dish()
    {
        var salted = Dish("Что-то с солью", 5, Salt, Herbs);
        var rice = Dish("Рис", 30, Rice);

        var ranked = MenuRanking.Rank(Input(salted, rice), [], HotOfferParameters.Default, Params);

        Assert.All(ranked, r => Assert.Equal(MenuGroup.Regular, r.Group));
        // Остальное меню — по тому, сколько можно приготовить.
        Assert.Equal([rice.Id, salted.Id], ranked.Select(r => r.DishId));
    }

    [Fact]
    public void Dish_that_saves_products_is_not_repeated_as_overstock_and_unavailable_dishes_are_skipped()
    {
        var both = Dish("Лосось со шпинатом", 10, Salmon);
        var stopped = Dish("В стопе", 0, Salmon);

        var ranked = MenuRanking.Rank(Input(both, stopped), [Offer(both, 50m), Offer(stopped, 90m)], HotOfferParameters.Default, Params);

        var only = Assert.Single(ranked);
        Assert.Equal(MenuGroup.Saves, only.Group);
    }

    [Fact]
    public void Low_score_offer_falls_through_to_the_other_groups()
    {
        var dish = Dish("Лосось", 10, Salmon);

        var ranked = Assert.Single(MenuRanking.Rank(Input(dish), [Offer(dish, 3m)], HotOfferParameters.Default, Params));

        Assert.Equal(MenuGroup.Overstock, ranked.Group);
    }
}
