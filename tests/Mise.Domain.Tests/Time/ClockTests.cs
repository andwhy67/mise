using Mise.Infrastructure.Time;

namespace Mise.Domain.Tests.Time;

public class ClockTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 12, 8, 30, 0, TimeSpan.Zero);

    [Fact]
    public void SystemClock_returns_utc()
    {
        var now = new SystemClock().UtcNow;

        Assert.Equal(TimeSpan.Zero, now.Offset);
    }

    [Fact]
    public void FakeClock_returns_initial_time()
    {
        var clock = new FakeClock(Start);

        Assert.Equal(Start, clock.UtcNow);
    }

    [Fact]
    public void FakeClock_normalizes_to_utc()
    {
        var moscow = new DateTimeOffset(2026, 10, 12, 11, 30, 0, TimeSpan.FromHours(3));

        var clock = new FakeClock(moscow);

        Assert.Equal(TimeSpan.Zero, clock.UtcNow.Offset);
        Assert.Equal(Start, clock.UtcNow);
    }

    [Fact]
    public void FakeClock_advance_moves_time_forward()
    {
        var clock = new FakeClock(Start);

        clock.Advance(TimeSpan.FromHours(6));

        Assert.Equal(Start.AddHours(6), clock.UtcNow);
    }

    [Fact]
    public void FakeClock_advance_rejects_negative_delta()
    {
        var clock = new FakeClock(Start);

        Assert.Throws<ArgumentOutOfRangeException>(() => clock.Advance(TimeSpan.FromMinutes(-1)));
    }

    [Fact]
    public void FakeClock_set_replaces_time()
    {
        var clock = new FakeClock(Start);
        var later = Start.AddDays(2);

        clock.Set(later);

        Assert.Equal(later, clock.UtcNow);
    }
}
