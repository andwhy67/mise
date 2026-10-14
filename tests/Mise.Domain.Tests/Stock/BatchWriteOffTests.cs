using Mise.Domain.Common;
using Mise.Domain.Stock;

namespace Mise.Domain.Tests.Stock;

public class BatchWriteOffTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 12, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void MarkExpired_moves_active_batch_to_expired_once_its_time_has_come()
    {
        var batch = FefoAllocatorTests.NewBatch(100m, 1);

        Assert.False(batch.MarkExpired(Now));
        Assert.Equal(BatchStatus.Active, batch.Status);

        Assert.True(batch.MarkExpired(Now.AddHours(1)));
        Assert.Equal(BatchStatus.Expired, batch.Status);
        Assert.Equal(100m, batch.RemainingQty);

        Assert.False(batch.MarkExpired(Now.AddHours(2)));
    }

    [Fact]
    public void MarkExpired_ignores_depleted_batch()
    {
        var batch = FefoAllocatorTests.NewBatch(100m, 1);
        batch.Consume(100m, Now);

        Assert.False(batch.MarkExpired(Now.AddHours(5)));
        Assert.Equal(BatchStatus.Depleted, batch.Status);
    }

    [Fact]
    public void Full_write_off_by_default_takes_whole_remainder()
    {
        var batch = FefoAllocatorTests.NewBatch(100m, 1);
        batch.Consume(30m, Now);
        batch.MarkExpired(Now.AddHours(1));

        var written = batch.WriteOff(null);

        Assert.Equal(70m, written);
        Assert.Equal(0m, batch.RemainingQty);
        Assert.Equal(BatchStatus.WrittenOff, batch.Status);
    }

    [Fact]
    public void Partial_write_off_keeps_status()
    {
        var batch = FefoAllocatorTests.NewBatch(100m, 10);

        var written = batch.WriteOff(25m);

        Assert.Equal(25m, written);
        Assert.Equal(75m, batch.RemainingQty);
        Assert.Equal(BatchStatus.Active, batch.Status);
    }

    [Fact]
    public void Writing_off_more_than_remaining_is_rejected_and_nothing_changes()
    {
        var batch = FefoAllocatorTests.NewBatch(100m, 10);

        var ex = Assert.Throws<DomainException>(() => batch.WriteOff(100.5m));

        Assert.Equal(Batch.Errors.InsufficientQuantity, ex.Code);
        Assert.Equal(100m, batch.RemainingQty);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Non_positive_quantity_is_rejected(int quantity)
    {
        var batch = FefoAllocatorTests.NewBatch(100m, 10);

        var ex = Assert.Throws<DomainException>(() => batch.WriteOff(quantity));

        Assert.Equal(Batch.Errors.InvalidQuantity, ex.Code);
    }

    [Fact]
    public void Depleted_or_written_off_batch_cannot_be_written_off()
    {
        var depleted = FefoAllocatorTests.NewBatch(100m, 10);
        depleted.Consume(100m, Now);
        var writtenOff = FefoAllocatorTests.NewBatch(100m, 10);
        writtenOff.WriteOff(null);

        Assert.Equal(Batch.Errors.NotWritable, Assert.Throws<DomainException>(() => depleted.WriteOff(null)).Code);
        Assert.Equal(Batch.Errors.NotWritable, Assert.Throws<DomainException>(() => writtenOff.WriteOff(null)).Code);
    }

    [Fact]
    public void Write_off_movements_have_negative_delta_and_reason()
    {
        var batch = FefoAllocatorTests.NewBatch(100m, 10);

        var expiry = StockMovement.ExpiryWriteOff(Guid.NewGuid(), batch, 40m, Now);
        var manual = StockMovement.ManualWriteOff(Guid.NewGuid(), batch, 10m, "Упала на пол", Now);

        Assert.Equal(MovementType.ExpiryWriteOff, expiry.Type);
        Assert.Equal(-40m, expiry.QtyDelta);
        Assert.False(string.IsNullOrWhiteSpace(expiry.Reason));
        Assert.Equal(MovementType.ManualWriteOff, manual.Type);
        Assert.Equal(-10m, manual.QtyDelta);
        Assert.Equal("Упала на пол", manual.Reason);
        Assert.Equal(batch.Id, manual.BatchId);
        Assert.Equal(batch.ProductId, manual.ProductId);
    }
}
