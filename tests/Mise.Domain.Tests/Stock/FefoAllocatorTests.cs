using Mise.Domain.Expiry;
using Mise.Domain.Stock;

namespace Mise.Domain.Tests.Stock;

public class FefoAllocatorTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 12, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid Product = Guid.NewGuid();

    internal static Batch NewBatch(decimal qty, double expiresInHours, Guid? productId = null, DateTimeOffset? now = null)
    {
        var at = now ?? Now;
        return Batch.Receive(Guid.CreateVersion7(), Guid.NewGuid(), productId ?? Product, at.AddDays(-1),
            new ExpiryResolution(at.AddHours(expiresInHours), ExpirySource.SupplyItemShelfLife), qty, at.AddDays(-1));
    }

    [Fact]
    public void Takes_from_the_earliest_expiring_batch_first()
    {
        var late = NewBatch(500m, 48);
        var early = NewBatch(500m, 6);

        var result = FefoAllocator.Allocate(300m, [late, early], Now);

        Assert.True(result.IsSatisfied);
        Assert.Equal([new BatchAllocation(early.Id, 300m)], result.Allocations);
    }

    [Fact]
    public void Spans_several_batches_in_expiry_order()
    {
        var a = NewBatch(100m, 5);
        var b = NewBatch(200m, 10);
        var c = NewBatch(1000m, 20);

        var result = FefoAllocator.Allocate(450m, [c, a, b], Now);

        Assert.Equal([new BatchAllocation(a.Id, 100m), new BatchAllocation(b.Id, 200m), new BatchAllocation(c.Id, 150m)],
            result.Allocations);
        Assert.Equal(0m, result.Shortage);
    }

    [Fact]
    public void Expired_batch_is_never_used_even_if_it_is_the_earliest()
    {
        var expired = NewBatch(1000m, -1);
        var fresh = NewBatch(100m, 24);

        var result = FefoAllocator.Allocate(150m, [expired, fresh], Now);

        Assert.False(result.IsSatisfied);
        Assert.Equal(100m, result.Available);
        Assert.Equal(50m, result.Shortage);
        Assert.Empty(result.Allocations);
    }

    [Fact]
    public void Batch_expiring_exactly_now_is_not_usable()
    {
        var expiringNow = NewBatch(100m, 0);

        var result = FefoAllocator.Allocate(1m, [expiringNow], Now);

        Assert.Equal(0m, result.Available);
        Assert.False(result.IsSatisfied);
    }

    [Fact]
    public void Exact_amount_consumes_everything()
    {
        var a = NewBatch(100m, 5);
        var b = NewBatch(50m, 8);

        var result = FefoAllocator.Allocate(150m, [a, b], Now);

        Assert.True(result.IsSatisfied);
        Assert.Equal(150m, result.Allocations.Sum(x => x.Quantity));
    }

    [Fact]
    public void Same_expiry_is_ordered_by_id_for_determinism()
    {
        var first = NewBatch(10m, 12);
        var second = NewBatch(10m, 12);
        var ordered = new[] { first, second }.OrderBy(b => b.Id).ToArray();

        var result = FefoAllocator.Allocate(15m, [ordered[1], ordered[0]], Now);

        Assert.Equal(ordered[0].Id, result.Allocations[0].BatchId);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Non_positive_requirement_is_rejected(decimal required)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => FefoAllocator.Allocate(required, [NewBatch(1m, 5)], Now));
    }
}
