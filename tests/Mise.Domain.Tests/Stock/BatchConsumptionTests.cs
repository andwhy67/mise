using Mise.Domain.Common;
using Mise.Domain.Stock;

namespace Mise.Domain.Tests.Stock;

public class BatchConsumptionTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 12, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Consume_reduces_remaining_and_depletes_at_zero()
    {
        var batch = FefoAllocatorTests.NewBatch(100m, 10);

        batch.Consume(40m, Now);
        Assert.Equal(60m, batch.RemainingQty);
        Assert.Equal(BatchStatus.Active, batch.Status);

        batch.Consume(60m, Now);
        Assert.Equal(0m, batch.RemainingQty);
        Assert.Equal(BatchStatus.Depleted, batch.Status);
        Assert.False(batch.IsUsableAt(Now));
    }

    [Fact]
    public void Consuming_more_than_remaining_is_rejected_and_nothing_changes()
    {
        var batch = FefoAllocatorTests.NewBatch(100m, 10);

        var ex = Assert.Throws<DomainException>(() => batch.Consume(100.001m, Now));

        Assert.Equal(Batch.Errors.InsufficientQuantity, ex.Code);
        Assert.Equal(100m, batch.RemainingQty);
    }

    [Fact]
    public void Expired_batch_cannot_be_consumed()
    {
        var batch = FefoAllocatorTests.NewBatch(100m, 1);

        var ex = Assert.Throws<DomainException>(() => batch.Consume(1m, Now.AddHours(1)));

        Assert.Equal(Batch.Errors.NotUsable, ex.Code);
    }

    [Fact]
    public void Return_reactivates_depleted_batch()
    {
        var batch = FefoAllocatorTests.NewBatch(100m, 10);
        batch.Consume(100m, Now);

        batch.Return(30m, Now);

        Assert.Equal(30m, batch.RemainingQty);
        Assert.Equal(BatchStatus.Active, batch.Status);
    }

    [Fact]
    public void Return_to_expired_batch_is_not_accepted()
    {
        var batch = FefoAllocatorTests.NewBatch(100m, 2);
        batch.Consume(50m, Now);

        Assert.False(batch.CanAcceptReturnAt(Now.AddHours(2)));
        var ex = Assert.Throws<DomainException>(() => batch.Return(10m, Now.AddHours(2)));
        Assert.Equal(Batch.Errors.NotReturnable, ex.Code);
    }

    [Fact]
    public void Return_cannot_exceed_initial_quantity()
    {
        var batch = FefoAllocatorTests.NewBatch(100m, 10);
        batch.Consume(10m, Now);

        var ex = Assert.Throws<DomainException>(() => batch.Return(11m, Now));

        Assert.Equal(Batch.Errors.InvalidQuantity, ex.Code);
    }

    [Fact]
    public void Movements_carry_signed_deltas_and_order_reference()
    {
        var batch = FefoAllocatorTests.NewBatch(100m, 10);
        var orderId = Guid.NewGuid();

        var consumption = StockMovement.OrderConsumption(Guid.NewGuid(), batch, 25m, orderId, Now);
        var giveBack = StockMovement.OrderCancelReturn(Guid.NewGuid(), batch, 25m, orderId, Now);

        Assert.Equal(-25m, consumption.QtyDelta);
        Assert.Equal(MovementType.OrderConsumption, consumption.Type);
        Assert.Equal(25m, giveBack.QtyDelta);
        Assert.Equal(MovementType.OrderCancelReturn, giveBack.Type);
        Assert.All([consumption, giveBack], m => Assert.Equal(orderId, m.OrderId));
    }
}
