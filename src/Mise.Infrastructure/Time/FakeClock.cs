using Mise.Domain.Abstractions;

namespace Mise.Infrastructure.Time;

/// <summary>
/// Управляемые часы для тестов и демо-режима «перемотки времени».
/// </summary>
public sealed class FakeClock : IClock
{
    private readonly Lock _sync = new();
    private DateTimeOffset _now;

    public FakeClock(DateTimeOffset start) => _now = start.ToUniversalTime();

    public DateTimeOffset UtcNow
    {
        get
        {
            lock (_sync) return _now;
        }
    }

    public void Set(DateTimeOffset value)
    {
        lock (_sync) _now = value.ToUniversalTime();
    }

    public void Advance(TimeSpan delta)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(delta, TimeSpan.Zero);
        lock (_sync) _now = _now.Add(delta);
    }
}
