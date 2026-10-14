using Mise.Domain.Alerts;
using Mise.Domain.Common;
using Mise.Domain.Expiry;

namespace Mise.Domain.Tests.Alerts;

public class AlertTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 12, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Expiring_alert_severity_follows_level_and_carries_dedup_key()
    {
        var batchId = Guid.NewGuid();
        var productId = Guid.NewGuid();

        var warning = Alert.ExpiringSoon(Guid.NewGuid(), batchId, productId, "Сливки", ShelfLifeStatus.Warning, 20m, Now);
        var critical = Alert.ExpiringSoon(Guid.NewGuid(), batchId, productId, "Сливки", ShelfLifeStatus.Critical, 3.5m, Now);

        Assert.Equal(AlertSeverity.Warning, warning.Severity);
        Assert.Equal(AlertSeverity.Critical, critical.Severity);
        Assert.Equal(AlertKeys.Batch(batchId, ShelfLifeStatus.Warning), warning.DedupKey);
        Assert.NotEqual(warning.DedupKey, critical.DedupKey);
        Assert.Contains("Сливки", critical.Message);
    }

    [Fact]
    public void Expiring_alert_rejects_non_expiring_levels()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            Alert.ExpiringSoon(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Сливки", ShelfLifeStatus.Fresh, 30m, Now));
    }

    [Fact]
    public void Expired_alert_is_critical_until_written_off()
    {
        var onShelf = Alert.Expired(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Лосось", 300m, MeasureUnit.Gram, writtenOff: false, Now);
        var writtenOff = Alert.Expired(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Лосось", 300m, MeasureUnit.Gram, writtenOff: true, Now);

        Assert.Equal(AlertSeverity.Critical, onShelf.Severity);
        Assert.Equal(AlertSeverity.Warning, writtenOff.Severity);
        Assert.Equal(AlertType.Expired, writtenOff.Type);
        Assert.Contains("300 г", writtenOff.Message);
    }

    [Fact]
    public void Low_stock_is_critical_when_nothing_is_left()
    {
        var some = Alert.LowStock(Guid.NewGuid(), Guid.NewGuid(), "Бекон", 100m, 500m, MeasureUnit.Gram, Now);
        var none = Alert.LowStock(Guid.NewGuid(), Guid.NewGuid(), "Бекон", 0m, 500m, MeasureUnit.Gram, Now);

        Assert.Equal(AlertSeverity.Warning, some.Severity);
        Assert.Equal(AlertSeverity.Critical, none.Severity);
        Assert.Equal(some.DedupKey, AlertKeys.LowStock(some.ProductId!.Value));
    }

    [Fact]
    public void Acknowledge_and_resolve_are_idempotent()
    {
        var alert = Alert.LowStock(Guid.NewGuid(), Guid.NewGuid(), "Бекон", 0m, 500m, MeasureUnit.Gram, Now);

        alert.Acknowledge(Now.AddMinutes(1));
        alert.Acknowledge(Now.AddMinutes(5));
        alert.Resolve(Now.AddMinutes(2));
        alert.Resolve(Now.AddMinutes(9));

        Assert.Equal(Now.AddMinutes(1), alert.AcknowledgedAt);
        Assert.Equal(Now.AddMinutes(2), alert.ResolvedAt);
        Assert.False(alert.IsActive);
    }

    [Fact]
    public void Stop_list_alerts_are_events_without_dedup_key()
    {
        var on = Alert.StopListOn(Guid.NewGuid(), Guid.NewGuid(), "Карбонара", "Бекон", Now);
        var off = Alert.StopListOff(Guid.NewGuid(), Guid.NewGuid(), "Карбонара", Now);

        Assert.Null(on.DedupKey);
        Assert.Equal(AlertSeverity.Warning, on.Severity);
        Assert.Contains("Бекон", on.Message);
        Assert.Equal(AlertSeverity.Info, off.Severity);
        Assert.Equal(AlertType.StopListOff, off.Type);
    }
}
