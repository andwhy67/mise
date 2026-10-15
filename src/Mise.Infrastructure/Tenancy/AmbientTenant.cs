using Mise.Application.Abstractions;

namespace Mise.Infrastructure.Tenancy;

/// <summary>
/// Ресторан текущего потока выполнения (AsyncLocal): переходит во все await внутри запроса или фоновой задачи
/// и не протекает в соседние запросы. Синглтон — его читают и scoped DbContext, и синглтон-буфер realtime.
/// </summary>
public sealed class AmbientTenant : ITenant
{
    private static readonly AsyncLocal<Guid?> Current = new();

    public Guid? RestaurantId => Current.Value;

    public IDisposable Use(Guid restaurantId)
    {
        var previous = Current.Value;
        Current.Value = restaurantId;
        return new Restore(previous);
    }

    private sealed class Restore(Guid? previous) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            Current.Value = previous;
        }
    }
}
