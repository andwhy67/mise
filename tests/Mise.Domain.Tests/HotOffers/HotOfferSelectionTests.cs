using Mise.Domain.Common;
using Mise.Domain.HotOffers;

namespace Mise.Domain.Tests.HotOffers;

public class HotOfferSelectionTests
{
    private static readonly HotOfferParameters Parameters = HotOfferParameters.Default with { TopN = 3 };

    [Fact]
    public void New_offer_needs_enter_threshold_and_existing_one_stays_until_exit_threshold()
    {
        var kept = Offer(12m);
        var newcomer = Offer(12m);
        var dropped = Offer(9m);
        var strong = Offer(40m);

        var selected = HotOfferSelector.Select([strong, kept, newcomer, dropped], previous: [kept.DishId, dropped.DishId], Parameters);

        Assert.Equal([strong.DishId, kept.DishId], selected.Select(o => o.DishId));
    }

    [Fact]
    public void Top_n_is_sorted_by_score_then_by_id_for_stability()
    {
        var offers = new[] { Offer(20m), Offer(50m), Offer(20m), Offer(30m), Offer(90m) };

        var selected = HotOfferSelector.Select(offers, previous: [], Parameters);

        Assert.Equal([90m, 50m, 30m], selected.Select(o => o.Score));
        var tied = offers.Where(o => o.Score == 20m).OrderBy(o => o.DishId).ToList();
        var all = HotOfferSelector.Select(offers, [], Parameters with { TopN = 10 });
        Assert.Equal(tied.Select(o => o.DishId), all.Skip(3).Select(o => o.DishId));
    }

    [Fact]
    public void Parameters_are_validated()
    {
        Assert.Throws<ArgumentException>(() => (HotOfferParameters.Default with { ExitThreshold = 20 }).Validate());
        Assert.Throws<ArgumentException>(() => (HotOfferParameters.Default with { HorizonShelfFraction = 0 }).Validate());
        Assert.Throws<ArgumentException>(() => (HotOfferParameters.Default with { TopN = 0 }).Validate());
        HotOfferParameters.Default.Validate();
    }

    [Fact]
    public void Explanation_reads_like_the_plan_example()
    {
        var offer = new HotOffer(Guid.NewGuid(), "Паста в сливках", 13, 84m, 1m,
        [
            new HotOfferReason(Guid.NewGuid(), "Сливки 33%", MeasureUnit.Milliliter, 2000m, null, 6m,
                DateTimeOffset.UnixEpoch, 150m, 13, 0.84m, 1m, 0.84m),
            new HotOfferReason(Guid.NewGuid(), "Шпинат", MeasureUnit.Gram, 600m, 450m, 0.5m,
                DateTimeOffset.UnixEpoch, 80m, 7, 0.9m, 0.5m, 0.72m),
        ]);

        var text = HotOfferExplainer.Explain(offer);

        Assert.Equal("Сливки 33%: 2 л истекает через 6 ч; в порции 150 мл → блюдо поможет списать до 13 порций сырья.",
            text.Reasons[0]);
        Assert.Equal("Шпинат: 600 г истекает через 30 мин, из них не успеет разойтись ≈450 г; в порции 80 г → блюдо поможет списать до 7 порций сырья.",
            text.Reasons[1]);
        Assert.Equal("Горит: Сливки 33% — 6 ч, Шпинат — 30 мин", text.Headline);
        Assert.Equal(text.Reasons[0] + " " + text.Reasons[1], text.Explanation);
    }

    private static HotOffer Offer(decimal score) => new(Guid.NewGuid(), "Блюдо", 5, score, 1m, []);
}
