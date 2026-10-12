using Mise.Domain.Common;
using Mise.Domain.Expiry;

namespace Mise.Domain.Tests.Expiry;

public class ExpiryPolicyTests
{
    private static readonly DateTimeOffset ReceivedAt = new(2026, 10, 12, 8, 30, 0, TimeSpan.Zero);

    [Fact]
    public void Explicit_date_has_top_priority()
    {
        var explicitDate = ReceivedAt.AddDays(3);

        var result = ExpiryPolicy.Resolve(ReceivedAt, explicitDate, itemShelfLifeHours: 24, productDefaultShelfLifeHours: 48);

        Assert.Equal(explicitDate, result.ExpiresAt);
        Assert.Equal(ExpirySource.ExplicitDate, result.Source);
    }

    [Fact]
    public void Item_shelf_life_is_counted_from_supply_creation()
    {
        var result = ExpiryPolicy.Resolve(ReceivedAt, explicitExpiresAt: null, itemShelfLifeHours: 168, productDefaultShelfLifeHours: 48);

        Assert.Equal(ReceivedAt.AddHours(168), result.ExpiresAt);
        Assert.Equal(ExpirySource.SupplyItemShelfLife, result.Source);
    }

    [Fact]
    public void Product_default_is_used_when_item_has_no_expiry_data()
    {
        var result = ExpiryPolicy.Resolve(ReceivedAt, explicitExpiresAt: null, itemShelfLifeHours: null, productDefaultShelfLifeHours: 72);

        Assert.Equal(ReceivedAt.AddHours(72), result.ExpiresAt);
        Assert.Equal(ExpirySource.ProductDefault, result.Source);
    }

    [Fact]
    public void Missing_all_sources_is_rejected()
    {
        var ex = Assert.Throws<DomainException>(() =>
            ExpiryPolicy.Resolve(ReceivedAt, explicitExpiresAt: null, itemShelfLifeHours: null, productDefaultShelfLifeHours: null));

        Assert.Equal(ExpiryPolicy.Errors.Unresolvable, ex.Code);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void Explicit_date_not_after_receipt_is_rejected(int hoursAfterReceipt)
    {
        var ex = Assert.Throws<DomainException>(() =>
            ExpiryPolicy.Resolve(ReceivedAt, ReceivedAt.AddHours(hoursAfterReceipt), null, 48));

        Assert.Equal(ExpiryPolicy.Errors.NotAfterReceipt, ex.Code);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Non_positive_item_shelf_life_is_rejected(int hours)
    {
        var ex = Assert.Throws<DomainException>(() => ExpiryPolicy.Resolve(ReceivedAt, null, hours, 48));

        Assert.Equal(ExpiryPolicy.Errors.InvalidShelfLife, ex.Code);
    }

    [Fact]
    public void Explicit_date_is_normalized_to_utc()
    {
        var moscow = new DateTimeOffset(2026, 10, 15, 11, 30, 0, TimeSpan.FromHours(3));

        var result = ExpiryPolicy.Resolve(ReceivedAt, moscow, null, null);

        Assert.Equal(TimeSpan.Zero, result.ExpiresAt.Offset);
        Assert.Equal(new DateTimeOffset(2026, 10, 15, 8, 30, 0, TimeSpan.Zero), result.ExpiresAt);
    }
}
