using Mise.Domain.Common;
using Mise.Domain.Expiry;
using Mise.Domain.Stock;

namespace Mise.Domain.Tests.Stock;

/// <summary>
/// Деньги в журнале движений: каждая операция записывает свою стоимость по цене конкретной партии (FEFO)
/// в момент операции — отчёты в ₽ не пересчитываются задним числом.
/// </summary>
public sealed class StockCostTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 15, 9, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Window = TimeSpan.FromHours(48);

    /// <summary>Лосось: 0,9 ₽ за грамм (900 ₽/кг).</summary>
    private static Batch Salmon(decimal qty = 2000m, double expiresInHours = 72, decimal? unitCost = 0.9m) =>
        Batch.Receive(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Now.AddDays(-1),
            new ExpiryResolution(Now.AddHours(expiresInHours), ExpirySource.SupplyItemShelfLife), qty, Now.AddDays(-1), unitCost);

    [Fact]
    public void Receipt_costs_the_whole_batch()
    {
        var movement = StockMovement.Receipt(Guid.NewGuid(), Salmon(), Guid.NewGuid(), Now);

        Assert.Equal(1800m, movement.Cost);
        Assert.False(movement.Rescued);
    }

    [Fact]
    public void Order_consumption_costs_by_the_batch_price_and_is_negative_like_the_quantity()
    {
        var movement = StockMovement.OrderConsumption(Guid.NewGuid(), Salmon(), 250m, Guid.NewGuid(), Now, Window);

        Assert.Equal(-225m, movement.Cost);
    }

    [Theory]
    [InlineData(47.9, true)]
    [InlineData(48, true)]
    [InlineData(48.1, false)]
    public void Consumption_from_a_batch_close_to_expiry_counts_as_rescued(double hoursLeft, bool rescued)
    {
        var movement = StockMovement.OrderConsumption(Guid.NewGuid(), Salmon(expiresInHours: hoursLeft), 100m, Guid.NewGuid(), Now, Window);

        Assert.Equal(rescued, movement.Rescued);
    }

    [Fact]
    public void Write_offs_and_returns_carry_cost_with_the_sign_of_the_quantity()
    {
        var batch = Salmon();

        Assert.Equal(-90m, StockMovement.ExpiryWriteOff(Guid.NewGuid(), batch, 100m, Now).Cost);
        Assert.Equal(-45m, StockMovement.ManualWriteOff(Guid.NewGuid(), batch, 50m, "Брак", Now).Cost);
        Assert.Equal(90m, StockMovement.OrderCancelReturn(Guid.NewGuid(), batch, 100m, Guid.NewGuid(), Now).Cost);
    }

    [Fact]
    public void Batch_without_price_has_no_cost_rather_than_zero()
    {
        var movement = StockMovement.OrderConsumption(Guid.NewGuid(), Salmon(unitCost: null), 100m, Guid.NewGuid(), Now, Window);

        Assert.Null(movement.Cost);
    }

    [Fact]
    public void Cost_is_rounded_to_kopecks()
    {
        // 0,0372 ₽/г (37,2 ₽/кг) × 333 г = 12,3876 ₽.
        var movement = StockMovement.OrderConsumption(Guid.NewGuid(), Salmon(unitCost: 0.0372m), 333m, Guid.NewGuid(), Now, Window);

        Assert.Equal(-12.39m, movement.Cost);
    }

    [Fact]
    public void Negative_price_is_rejected()
    {
        var ex = Assert.Throws<DomainException>(() => Salmon(unitCost: -1m));

        Assert.Equal(Batch.Errors.InvalidCost, ex.Code);
    }
}
