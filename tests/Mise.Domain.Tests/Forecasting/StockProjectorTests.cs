using Mise.Domain.Common;
using Mise.Domain.Forecasting;

namespace Mise.Domain.Tests.Forecasting;

public class StockProjectorTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 12, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Step = TimeSpan.FromMinutes(15);

    [Fact]
    public void Consumption_runs_the_stock_out_and_time_is_interpolated()
    {
        var projection = Project([Batch(1000m, 100)], perHour: 100m, horizonHours: 24);

        Assert.Equal(Now.AddHours(10), projection.RunsOutAt);
        Assert.Equal(1000m, projection.Consumed);
        Assert.Equal(0m, projection.Wasted);
        Assert.Equal(1400m, projection.UnmetDemand); // 24 ч × 100 − 1000
        Assert.Equal(Now.AddHours(7), projection.FirstTimeBelow(300m)!.Value.At); // 1000 − 100 × 7 = 300
    }

    [Fact]
    public void Batches_are_used_fefo_and_the_unused_rest_expires()
    {
        // Ранняя партия 500 истекает через 2 ч, спрос 100/ч → 200 съедят, 300 пропадут; дальше работает поздняя.
        var projection = Project([Batch(800m, 100), Batch(500m, 2)], perHour: 100m, horizonHours: 6);

        Assert.Equal(300m, projection.Wasted);
        Assert.Equal(600m, projection.Consumed);
        Assert.Null(projection.RunsOutAt);
        Assert.Equal(400m, projection.UsableAt(Now.AddHours(6)));
        var drop = projection.FirstTimeBelow(1000m)!.Value;
        Assert.Equal(Now.AddHours(2), drop.At);
        Assert.Equal(ProjectionCause.Expiry, drop.Cause);
    }

    [Fact]
    public void Without_demand_only_expiry_changes_the_stock()
    {
        var projection = Project([Batch(400m, 5)], perHour: 0m, horizonHours: 24);

        var crossing = projection.FirstTimeBelow(100m)!.Value;
        Assert.Equal(Now.AddHours(5), crossing.At);
        Assert.Equal(ProjectionCause.Expiry, crossing.Cause);
        Assert.Equal(400m, projection.Wasted);
        Assert.Null(projection.RunsOutAt); // спроса нет — «исчерпания» нет, есть только потеря
    }

    [Fact]
    public void Nothing_beyond_the_horizon()
    {
        var projection = Project([Batch(10_000m, 500)], perHour: 10m, horizonHours: 24);

        Assert.Null(projection.FirstTimeBelow(100m));
        Assert.Equal(9760m, projection.UsableAt(Now.AddHours(24)));
    }

    [Fact]
    public void Already_below_threshold_crosses_now()
    {
        var projection = Project([Batch(50m, 100)], perHour: 1m, horizonHours: 24);

        Assert.Equal(Now, projection.FirstTimeBelow(80m)!.Value.At);
    }

    private static StockProjection Project(IReadOnlyList<ForecastBatch> batches, decimal perHour, int horizonHours) =>
        StockProjector.Project(batches, new ConsumptionRate(perHour, null, ForecastConfidence.Medium, 48m), Now,
            TimeSpan.FromHours(horizonHours), Step);

    private static ForecastBatch Batch(decimal qty, double hoursLeft) => new(Guid.NewGuid(), Now.AddHours(hoursLeft), qty);
}

public class StopListForecasterTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 12, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Dish_eta_is_the_earliest_ingredient_that_cannot_cover_a_portion()
    {
        var bacon = Guid.NewGuid();
        var pasta = Guid.NewGuid();
        var projections = new Dictionary<Guid, StockProjection>
        {
            // Бекон: 300 г, спрос 30 г/ч → < 60 г через 8 ч.
            [bacon] = StockProjector.Project([new(Guid.NewGuid(), Now.AddHours(100), 300m)], Rate(30m), Now, TimeSpan.FromHours(24), TimeSpan.FromMinutes(15)),
            // Паста хватает надолго.
            [pasta] = StockProjector.Project([new(Guid.NewGuid(), Now.AddHours(1000), 5000m)], Rate(50m), Now, TimeSpan.FromHours(24), TimeSpan.FromMinutes(15)),
        };
        var carbonara = new ForecastDish(Guid.NewGuid(), "Карбонара", 5, [new(bacon, 60m), new(pasta, 120m)]);
        var plain = new ForecastDish(Guid.NewGuid(), "Паста", 40, [new(pasta, 120m)]);
        var stopped = new ForecastDish(Guid.NewGuid(), "Уже в стопе", 0, [new(bacon, 500m)]);

        var forecast = StopListForecaster.Forecast([carbonara, plain, stopped], projections, Now);

        var item = Assert.Single(forecast);
        Assert.Equal(carbonara.Id, item.DishId);
        Assert.Equal(bacon, item.LimitingProductId);
        Assert.Equal(ProjectionCause.Consumption, item.Cause);
        Assert.InRange(item.Eta, Now.AddHours(8).AddMinutes(-1), Now.AddHours(8).AddMinutes(1));
    }

    private static ConsumptionRate Rate(decimal perHour) => new(perHour, null, ForecastConfidence.High, 100m);
}

public class SupplySuggesterTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 12, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Horizon = TimeSpan.FromHours(72);

    [Fact]
    public void Need_covers_demand_and_safety_minus_what_survives_rounded_to_packs()
    {
        // Спрос 10/ч → 720 за 72 ч; страховой = max(min 200, 10 × 12 = 120) = 200; на складе 300 (доживёт).
        var suggestion = Suggest(Product(minStock: 200, pack: 250, shelfLife: 168), [Batch(300m, 500)], perHour: 10m)!;

        Assert.Equal(720m, suggestion.ExpectedDemand);
        Assert.Equal(300m, suggestion.Projected);
        Assert.Equal(200m, suggestion.Safety);
        Assert.Equal(620m, suggestion.Need);
        Assert.Equal(750m, suggestion.SuggestedQty);
        Assert.Equal(Now.AddHours(30), suggestion.RunsOutAt);
        Assert.Equal(SuggestionUrgency.Normal, suggestion.Urgency);
        Assert.Contains(suggestion.Reasons, r => r.Contains("исчерпается через 30 ч"));
    }

    [Fact]
    public void Quantity_is_capped_by_what_can_be_used_before_it_spoils()
    {
        // Срок 24 ч, спрос 10/ч → больше 240 не успеем использовать (ниже минимума cap не опускается).
        var suggestion = Suggest(Product(minStock: 100, pack: 100, shelfLife: 24), [], perHour: 10m)!;

        Assert.Equal(240m, suggestion.Cap);
        Assert.Equal(300m, suggestion.SuggestedQty); // ⌈240 / 100⌉ × 100
        Assert.True(suggestion.CappedByShelfLife);
        Assert.Contains(suggestion.Reasons, r => r.Contains("ограничен сроком годности"));
        Assert.Equal(SuggestionUrgency.Critical, suggestion.Urgency); // склад пуст
    }

    [Fact]
    public void Without_history_it_tops_up_to_minimum_stock()
    {
        var suggestion = Suggest(Product(minStock: 1000, pack: 500, shelfLife: 72), [Batch(300m, 50)], perHour: 0m)!;

        Assert.Equal(0m, suggestion.ExpectedDemand);
        Assert.Equal(1000m, suggestion.SuggestedQty); // 300 истекут через 50 ч неиспользованными → нужно 1000 = 2 упаковки
        Assert.Equal(SuggestionUrgency.High, suggestion.Urgency); // ниже минимума
        Assert.Contains(suggestion.Reasons, r => r.Contains("ниже минимального остатка"));
        Assert.Contains(suggestion.Reasons, r => r.Contains("нет истории расхода"));
    }

    [Fact]
    public void Stock_that_will_expire_unused_does_not_count_and_is_reported()
    {
        // 1000 истекают через 10 ч, спрос 10/ч съест 100 — 900 пропадут.
        var suggestion = Suggest(Product(minStock: 100, pack: 100, shelfLife: 168), [Batch(1000m, 10)], perHour: 10m)!;

        Assert.Equal(900m, suggestion.ExpectedWaste);
        Assert.Equal(100m, suggestion.Projected);
        Assert.Contains(suggestion.Reasons, r => r.Contains("истечёт неиспользованным"));
        Assert.Equal(SuggestionUrgency.Critical, suggestion.Urgency); // кончится через 10 ч
    }

    [Fact]
    public void Enough_stock_means_no_suggestion()
    {
        Assert.Null(Suggest(Product(minStock: 100, pack: 100, shelfLife: 168), [Batch(5000m, 500)], perHour: 10m));
    }

    private static SupplySuggestion? Suggest(ForecastProduct product, IReadOnlyList<ForecastBatch> batches, decimal perHour)
    {
        var rate = new ConsumptionRate(perHour, null, perHour > 0 ? ForecastConfidence.High : ForecastConfidence.Low, perHour > 0 ? 100m : 0m);
        var projection = StockProjector.Project(batches, rate, Now, Horizon, TimeSpan.FromMinutes(15));
        return SupplySuggester.Suggest(product, projection, rate, Now, Horizon, SupplySuggester.DefaultSafetyHours);
    }

    private static ForecastProduct Product(decimal minStock, decimal pack, int? shelfLife) =>
        new(Guid.NewGuid(), "Сливки", MeasureUnit.Milliliter, minStock, pack, shelfLife);

    private static ForecastBatch Batch(decimal qty, double hoursLeft) => new(Guid.NewGuid(), Now.AddHours(hoursLeft), qty);
}
