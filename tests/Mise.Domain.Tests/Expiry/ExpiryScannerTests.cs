using Mise.Domain.Alerts;
using Mise.Domain.Expiry;
using Mise.Domain.Stock;
using Mise.Domain.Tests.Stock;

namespace Mise.Domain.Tests.Expiry;

public class ExpiryScannerTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 12, 12, 0, 0, TimeSpan.Zero);
    private static readonly ShelfLifeThresholds Thresholds = ShelfLifeThresholds.Default;
    private static readonly HashSet<string> NoneRaised = [];

    // NewBatch: получена за сутки до Now, истекает через N часов → полный срок 24 + N часов.

    [Fact]
    public void Fresh_batch_produces_nothing()
    {
        var batch = FefoAllocatorTests.NewBatch(100m, 48);

        var plan = ExpiryScanner.Plan([batch], NoneRaised, Now, Thresholds, autoWriteOff: true);

        Assert.True(plan.IsEmpty);
    }

    [Fact]
    public void Batch_crossing_warning_threshold_raises_warning_signal()
    {
        var batch = FefoAllocatorTests.NewBatch(100m, 20); // 20 ч из 44: ≤50% и ≤24 ч

        var plan = ExpiryScanner.Plan([batch], NoneRaised, Now, Thresholds, autoWriteOff: true);

        var signal = Assert.Single(plan.Signals);
        Assert.Equal(new ExpirySignal(batch.Id, batch.ProductId, ShelfLifeStatus.Warning, 20m), signal);
        Assert.Empty(plan.ToExpire);
        Assert.Empty(plan.ToWriteOff);
    }

    [Fact]
    public void Tick_that_skipped_warning_raises_only_critical()
    {
        var batch = FefoAllocatorTests.NewBatch(100m, 3);

        var plan = ExpiryScanner.Plan([batch], NoneRaised, Now, Thresholds, autoWriteOff: true);

        Assert.Equal(ShelfLifeStatus.Critical, Assert.Single(plan.Signals).Level);
    }

    [Fact]
    public void Already_raised_level_is_not_repeated()
    {
        var batch = FefoAllocatorTests.NewBatch(100m, 3);
        var raised = new HashSet<string> { AlertKeys.Batch(batch.Id, ShelfLifeStatus.Critical) };

        var plan = ExpiryScanner.Plan([batch], raised, Now, Thresholds, autoWriteOff: true);

        Assert.True(plan.IsEmpty);
    }

    [Fact]
    public void Critical_is_raised_after_warning_was_raised_earlier()
    {
        var batch = FefoAllocatorTests.NewBatch(100m, 3);
        var raised = new HashSet<string> { AlertKeys.Batch(batch.Id, ShelfLifeStatus.Warning) };

        var plan = ExpiryScanner.Plan([batch], raised, Now, Thresholds, autoWriteOff: true);

        Assert.Equal(ShelfLifeStatus.Critical, Assert.Single(plan.Signals).Level);
    }

    [Fact]
    public void Expired_active_batch_is_expired_written_off_and_signalled()
    {
        var batch = FefoAllocatorTests.NewBatch(100m, 1);
        var at = Now.AddHours(1);

        var plan = ExpiryScanner.Plan([batch], NoneRaised, at, Thresholds, autoWriteOff: true);

        Assert.Equal([batch], plan.ToExpire);
        Assert.Equal([batch], plan.ToWriteOff);
        var signal = Assert.Single(plan.Signals);
        Assert.Equal(ShelfLifeStatus.Expired, signal.Level);
        Assert.Equal(0m, signal.HoursLeft);
    }

    [Fact]
    public void Without_auto_write_off_batch_is_only_expired()
    {
        var batch = FefoAllocatorTests.NewBatch(100m, 1);

        var plan = ExpiryScanner.Plan([batch], NoneRaised, Now.AddHours(2), Thresholds, autoWriteOff: false);

        Assert.Equal([batch], plan.ToExpire);
        Assert.Empty(plan.ToWriteOff);
        Assert.Equal(ShelfLifeStatus.Expired, Assert.Single(plan.Signals).Level);
    }

    [Fact]
    public void Expired_leftover_is_written_off_when_auto_write_off_is_on()
    {
        var batch = FefoAllocatorTests.NewBatch(100m, 1);
        batch.MarkExpired(Now.AddHours(1));
        var raised = new HashSet<string> { AlertKeys.Batch(batch.Id, ShelfLifeStatus.Expired) };

        var plan = ExpiryScanner.Plan([batch], raised, Now.AddHours(2), Thresholds, autoWriteOff: true);

        Assert.Empty(plan.ToExpire);
        Assert.Equal([batch], plan.ToWriteOff);
        Assert.Empty(plan.Signals);
    }

    [Fact]
    public void Closed_batches_are_ignored()
    {
        var depleted = FefoAllocatorTests.NewBatch(100m, 1);
        depleted.Consume(100m, Now);
        var writtenOff = FefoAllocatorTests.NewBatch(100m, 1);
        writtenOff.WriteOff(null);

        var plan = ExpiryScanner.Plan([depleted, writtenOff], NoneRaised, Now.AddHours(5), Thresholds, autoWriteOff: true);

        Assert.True(plan.IsEmpty);
    }
}
