using Mise.Domain.Common;
using Mise.Domain.Forecasting;

namespace Mise.Domain.Tests.Forecasting;

public class EventPlannerTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 12, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Step = TimeSpan.FromMinutes(30);
    private static readonly Guid Cream = Guid.NewGuid();

    private static ConsumptionRate PerHour(decimal qty) => new(qty, null, ForecastConfidence.Medium, 72m);

    private static EventProduct Product(decimal qty, double expiresInHours, decimal perHour = 0m, decimal packSize = 500m, decimal? unitCost = null) =>
        new(Cream, "Сливки", MeasureUnit.Milliliter, packSize, unitCost,
            [new ForecastBatch(Guid.NewGuid(), Now.AddHours(expiresInHours), qty)], PerHour(perHour));

    private static IReadOnlyDictionary<Guid, EventProduct> One(EventProduct product) => new Dictionary<Guid, EventProduct> { [product.Id] = product };

    [Fact]
    public void Compares_the_event_with_what_is_left_after_regular_guests_and_rounds_purchase_to_packs()
    {
        // 1000 мл, обычные гости берут 10 мл/ч: к событию через 10 ч останется 900, банкету нужен литр.
        var plan = EventPlanner.Plan(new Dictionary<Guid, decimal> { [Cream] = 1000m }, One(Product(1000m, 200, perHour: 10m)), [],
            Now, Now.AddHours(10), Step);

        var line = Assert.Single(plan.Lines);
        Assert.Equal(900m, line.AvailableAtEvent);
        Assert.Equal(100m, line.Shortage);
        Assert.Equal(500m, line.BuyQty);
        Assert.False(plan.Feasible);
    }

    [Fact]
    public void Counts_what_the_event_rescues_from_expiring_unused()
    {
        // Без банкета 500 мл истекут через 20 ч; банкет через 10 ч заберёт 300 — их не придётся списывать.
        var plan = EventPlanner.Plan(new Dictionary<Guid, decimal> { [Cream] = 300m }, One(Product(500m, 20, unitCost: 0.5m)), [],
            Now, Now.AddHours(10), Step);

        var line = Assert.Single(plan.Lines);
        Assert.True(plan.Feasible);
        Assert.Equal(300m, line.RescuedQty);
        Assert.Equal(150m, plan.RescuedCost);
    }

    [Fact]
    public void Lists_dishes_that_go_to_stop_after_the_event()
    {
        var dish = new ForecastDish(Guid.NewGuid(), "Крем-суп", 10, [new ForecastIngredient(Cream, 100m)]);

        var plan = EventPlanner.Plan(new Dictionary<Guid, decimal> { [Cream] = 1000m }, One(Product(1000m, 200)), [dish],
            Now, Now.AddHours(5), Step);

        var stop = Assert.Single(plan.StopsAfter);
        Assert.Equal(10, stop.PortionsBefore);
    }
}
