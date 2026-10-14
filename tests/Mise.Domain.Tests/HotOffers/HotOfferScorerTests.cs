using Mise.Domain.Common;
using Mise.Domain.HotOffers;

namespace Mise.Domain.Tests.HotOffers;

/// <summary>Сценарии scoring из PLAN §9 (а)–(е) и дополнительные (ж)–(л). Обе версии скорера обязаны их проходить.</summary>
public class HotOfferScorerTests
{
    public static TheoryData<IHotOfferScorer> Scorers => [new HotOfferScorerV1(), new HotOfferScorerV2()];

    [Theory]
    [MemberData(nameof(Scorers))]
    public void A_burning_cream_puts_cream_dishes_above_dishes_without_it(IHotOfferScorer scorer)
    {
        var s = new HotOfferScenarioBuilder()
            .Product("Сливки", unit: MeasureUnit.Milliliter).Product("Паста", shelfLifeHours: 8760)
            .Batch("Сливки", 2000, hoursLeft: 6).Batch("Паста", 5000, hoursLeft: 4000)
            .Dish("Паста в сливках", 13, ("Паста", 100), ("Сливки", 150))
            .Dish("Паста с маслом", 50, ("Паста", 100));

        var offers = s.Score(scorer);

        var offer = Assert.Single(offers);
        Assert.Equal(s.Id("Паста в сливках"), offer.DishId);
        // H = min(72, 0,5 × 168) = 72; u = (1 − 6/72)² ≈ 0,84; V = 1; share = 1; cap = 1 → ≈ 84.
        Assert.InRange(offer.Score, 83.9m, 84.1m);
    }

    [Theory]
    [MemberData(nameof(Scorers))]
    public void B_big_batch_burns_harder_than_small_one_with_same_urgency(IHotOfferScorer scorer)
    {
        var s = new HotOfferScenarioBuilder()
            .Product("Лосось").Product("Креветки")
            .Batch("Лосось", 5000, hoursLeft: 8).Batch("Креветки", 200, hoursLeft: 8)
            .Dish("Лосось на гриле", 10, ("Лосось", 180))
            .Dish("Креветки на гриле", 10, ("Креветки", 20));

        Assert.True(s.ScoreOf("Лосось на гриле", scorer) > s.ScoreOf("Креветки на гриле", scorer));
    }

    [Theory]
    [MemberData(nameof(Scorers))]
    public void C_unavailable_dish_is_not_offered_and_takes_no_share(IHotOfferScorer scorer)
    {
        var s = new HotOfferScenarioBuilder()
            .Product("Сливки").Product("Креветки")
            .Batch("Сливки", 2000, hoursLeft: 6)
            .Dish("Суп-крем", 10, ("Сливки", 100))
            .Dish("Креветки в сливках", 0, ("Сливки", 300), ("Креветки", 100));

        var offers = s.Score(scorer);

        var offer = Assert.Single(offers);
        Assert.Equal(s.Id("Суп-крем"), offer.DishId);
        Assert.Equal(1m, offer.Reasons.Single().Share);
    }

    [Theory]
    [MemberData(nameof(Scorers))]
    public void D_shared_ingredient_is_split_and_bigger_share_ranks_higher(IHotOfferScorer scorer)
    {
        var s = new HotOfferScenarioBuilder()
            .Product("Сливки")
            .Batch("Сливки", 3000, hoursLeft: 10)
            .Dish("Соус", 10, ("Сливки", 150))
            .Dish("Суп", 10, ("Сливки", 50))
            .Dish("Омлет", 10, ("Сливки", 50));

        var offers = s.Score(scorer);

        Assert.Equal(s.Id("Соус"), offers[0].DishId);
        Assert.Equal(0.6m, offers[0].Reasons.Single().Share);
        Assert.Equal(0.2m, offers[1].Reasons.Single().Share);
        Assert.True(offers[0].Score > offers[1].Score);
        Assert.Equal(offers[1].Score, offers[2].Score);
    }

    [Theory]
    [MemberData(nameof(Scorers))]
    public void E_expired_batch_is_ignored(IHotOfferScorer scorer)
    {
        var s = new HotOfferScenarioBuilder()
            .Product("Сливки")
            .Batch("Сливки", 50_000, hoursLeft: -1)
            .Dish("Соус", 10, ("Сливки", 150));

        Assert.Empty(s.Score(scorer));
    }

    [Theory]
    [MemberData(nameof(Scorers))]
    public void F_two_burning_ingredients_give_a_bonus(IHotOfferScorer scorer)
    {
        var s = new HotOfferScenarioBuilder()
            .Product("Лосось").Product("Шпинат").Product("Паста", shelfLifeHours: 8760)
            .Batch("Лосось", 2000, hoursLeft: 30).Batch("Шпинат", 2000, hoursLeft: 30).Batch("Паста", 5000, hoursLeft: 4000)
            .Dish("Лосось со шпинатом", 10, ("Лосось", 180), ("Шпинат", 80))
            .Dish("Лосось с пастой", 10, ("Лосось", 180), ("Паста", 100))
            .Dish("Шпинат с пастой", 10, ("Шпинат", 80), ("Паста", 100));

        var offers = s.Score(scorer);

        Assert.Equal(s.Id("Лосось со шпинатом"), offers[0].DishId);
        Assert.Equal(2, offers[0].Reasons.Count);
        Assert.True(offers[0].Score > offers[1].Score);
    }

    [Theory]
    [MemberData(nameof(Scorers))]
    public void G_fresh_batch_beyond_horizon_gives_no_offer(IHotOfferScorer scorer)
    {
        var s = new HotOfferScenarioBuilder()
            .Product("Сливки")
            .Batch("Сливки", 2000, hoursLeft: 80) // H = 72
            .Dish("Соус", 10, ("Сливки", 150));

        Assert.Empty(s.Score(scorer));
    }

    [Theory]
    [MemberData(nameof(Scorers))]
    public void H_dish_with_a_single_portion_left_ranks_below_dish_with_many(IHotOfferScorer scorer)
    {
        var s = new HotOfferScenarioBuilder()
            .Product("Сливки")
            .Batch("Сливки", 2000, hoursLeft: 6)
            .Dish("Много порций", 6, ("Сливки", 100))
            .Dish("Одна порция", 1, ("Сливки", 100));

        var many = s.ScoreOf("Много порций", scorer);
        var one = s.ScoreOf("Одна порция", scorer);

        Assert.InRange(one, many / 3 - 0.2m, many / 3 + 0.2m); // cap = 1/3
    }

    [Theory]
    [MemberData(nameof(Scorers))]
    public void Score_never_decreases_as_expiry_approaches(IHotOfferScorer scorer)
    {
        var scores = new[] { 70.0, 50, 30, 12, 6, 2, 0.5 }.Select(hours => new HotOfferScenarioBuilder()
                .Product("Сливки").Batch("Сливки", 2000, hours).Dish("Соус", 10, ("Сливки", 150))
                .ScoreOf("Соус", scorer))
            .ToList();

        Assert.Equal(scores.Order(), scores);
        Assert.True(scores[^1] > scores[0]);
    }

    [Fact]
    public void I_v2_lowers_batch_that_demand_will_eat_before_expiry()
    {
        // Оба продукта: 500 г истекают через 6 ч. «Ходовой» расходуется 100 г/ч — успеет разойтись; «Редкий» — 10 г/ч.
        var s = new HotOfferScenarioBuilder()
            .Product("Ходовой", dailyConsumption: 2400).Product("Редкий", dailyConsumption: 240)
            .Batch("Ходовой", 500, hoursLeft: 6).Batch("Редкий", 500, hoursLeft: 6)
            .Dish("Блюдо из ходового", 10, ("Ходовой", 50))
            .Dish("Блюдо из редкого", 10, ("Редкий", 50));

        var v1 = new HotOfferScorerV1();
        var v2 = new HotOfferScorerV2();

        // Ходовой разойдётся целиком: V = 0 и множитель 0,5 → P = 0,25·T вместо 0,604·T в v1.
        var common1 = s.ScoreOf("Блюдо из ходового", v1);
        var common2 = s.ScoreOf("Блюдо из ходового", v2);
        Assert.InRange(common2 / common1, 0.40m, 0.43m);
        // Редкий почти весь пропадёт (440 из 500): множитель 0,94.
        var rare1 = s.ScoreOf("Блюдо из редкого", v1);
        var rare2 = s.ScoreOf("Блюдо из редкого", v2);
        Assert.InRange(rare2 / rare1, 0.93m, 0.95m);
        Assert.True(rare2 > 2 * common2);

        var rare = s.Score(v2).Single(o => o.DishId == s.Id("Блюдо из редкого")).Reasons.Single();
        Assert.Equal(440m, rare.UnsoldQty); // за 6 ч продастся 60 г
    }

    [Fact]
    public void V2_without_consumption_history_equals_v1()
    {
        var s = new HotOfferScenarioBuilder()
            .Product("Сливки").Product("Шпинат")
            .Batch("Сливки", 3000, hoursLeft: 20).Batch("Шпинат", 600, hoursLeft: 4)
            .Dish("Паста", 7, ("Сливки", 80), ("Шпинат", 50))
            .Dish("Суп", 20, ("Сливки", 100));

        Assert.Equal(
            s.Score(new HotOfferScorerV1()).Select(o => (o.DishId, o.Score)),
            s.Score(new HotOfferScorerV2()).Select(o => (o.DishId, o.Score)));
    }

    [Theory]
    [MemberData(nameof(Scorers))]
    public void Horizon_comes_from_product_norm_and_falls_back_to_batch_shelf_life(IHotOfferScorer scorer)
    {
        // Норма 168 ч → H = 72 ч: 4 ч до конца — срочно, даже если партия пришла со сроком всего 10 ч.
        var withNorm = new HotOfferScenarioBuilder()
            .Product("Сливки", shelfLifeHours: 168).Batch("Сливки", 2000, hoursLeft: 4, ageHours: 6).Dish("Соус", 10, ("Сливки", 150));
        // Нормы нет → H = 0,5 × срок партии (10 ч) = 5 ч: партия в горизонте, но срочность ниже.
        var withoutNorm = new HotOfferScenarioBuilder()
            .Product("Сливки", shelfLifeHours: null).Batch("Сливки", 2000, hoursLeft: 4, ageHours: 6).Dish("Соус", 10, ("Сливки", 150));

        Assert.InRange(withNorm.ScoreOf("Соус", scorer), 89m, 90m);       // (1 − 4/72)² ≈ 0,892
        Assert.InRange(withoutNorm.ScoreOf("Соус", scorer), 3.9m, 4.1m);  // (1 − 4/5)² = 0,04
    }

    [Fact]
    public void Reasons_are_ordered_by_contribution_and_carry_explainable_numbers()
    {
        var s = new HotOfferScenarioBuilder()
            .Product("Сливки", unit: MeasureUnit.Milliliter).Product("Шпинат")
            .Batch("Сливки", 1200, hoursLeft: 30).Batch("Сливки", 800, hoursLeft: 50).Batch("Шпинат", 600, hoursLeft: 4)
            .Dish("Паста", 7, ("Сливки", 150), ("Шпинат", 80));

        var offer = Assert.Single(s.Score());

        Assert.Equal(["Шпинат", "Сливки"], offer.Reasons.Select(r => r.ProductName));
        var cream = offer.Reasons[1];
        Assert.Equal(2000m, cream.AtRiskQty);              // обе партии в горизонте 72 ч
        Assert.Equal(30m, cream.HoursLeft);                 // по самой ранней
        Assert.Equal(7, cream.PortionsBurned);              // min(2000 / 150 = 13, maxPortions 7)
        Assert.Equal(HotOfferScenarioBuilder.Now.AddHours(30), cream.ExpiresAt);
    }
}
