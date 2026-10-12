using Mise.Domain.Common;
using Mise.Domain.Expiry;
using Mise.Domain.Stock;

namespace Mise.Domain.Tests.Stock;

public class BatchTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 12, 8, 0, 0, TimeSpan.Zero);

    private static Batch NewBatch(decimal qty = 1000m, double expiresInHours = 48, DateTimeOffset? receivedAt = null) =>
        Batch.Receive(
            id: Guid.NewGuid(),
            supplyItemId: Guid.NewGuid(),
            productId: Guid.NewGuid(),
            receivedAt: receivedAt ?? Now,
            expiry: new ExpiryResolution(Now.AddHours(expiresInHours), ExpirySource.SupplyItemShelfLife),
            quantity: qty,
            now: Now);

    [Fact]
    public void New_batch_is_active_with_full_quantity()
    {
        var batch = NewBatch(qty: 750m);

        Assert.Equal(BatchStatus.Active, batch.Status);
        Assert.Equal(750m, batch.InitialQty);
        Assert.Equal(750m, batch.RemainingQty);
        Assert.True(batch.IsUsableAt(Now));
        Assert.Equal(750m, batch.UsableQtyAt(Now));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Non_positive_quantity_is_rejected(decimal qty)
    {
        var ex = Assert.Throws<DomainException>(() => NewBatch(qty: qty));

        Assert.Equal(Batch.Errors.InvalidQuantity, ex.Code);
    }

    [Fact]
    public void Batch_already_expired_on_receipt_is_registered_as_expired()
    {
        // Поставка «задним числом»: срок истёк ещё до регистрации (assumptions A2).
        var batch = NewBatch(expiresInHours: -1, receivedAt: Now.AddDays(-3));

        Assert.Equal(BatchStatus.Expired, batch.Status);
        Assert.False(batch.IsUsableAt(Now));
        Assert.Equal(0m, batch.UsableQtyAt(Now));
        Assert.Equal(1000m, batch.RemainingQty);
    }

    [Fact]
    public void Batch_is_not_usable_from_the_expiry_moment()
    {
        var batch = NewBatch(expiresInHours: 10);

        Assert.True(batch.IsUsableAt(Now.AddHours(10).AddTicks(-1)));
        Assert.False(batch.IsUsableAt(Now.AddHours(10)));
        Assert.Equal(0m, batch.UsableQtyAt(Now.AddHours(10)));
    }

    [Fact]
    public void Receipt_movement_matches_batch()
    {
        var batch = NewBatch(qty: 500m);
        var supplyId = Guid.NewGuid();

        var movement = StockMovement.Receipt(Guid.NewGuid(), batch, supplyId, Now);

        Assert.Equal(MovementType.Receipt, movement.Type);
        Assert.Equal(500m, movement.QtyDelta);
        Assert.Equal(batch.Id, movement.BatchId);
        Assert.Equal(batch.ProductId, movement.ProductId);
        Assert.Equal(supplyId, movement.SupplyId);
        Assert.Equal(Now, movement.CreatedAt);
    }
}
