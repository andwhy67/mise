namespace Mise.Domain.Menu;

public enum StopReason
{
    Shortage,
    Manual,
}

/// <summary>Интервал нахождения блюда в стоп-листе (для истории и аналитики дефицита).</summary>
public sealed class StopListEntry
{
    private StopListEntry() { }

    public StopListEntry(Guid id, Guid dishId, StopReason reason, Guid? limitingProductId, DateTimeOffset startedAt)
    {
        Id = id;
        DishId = dishId;
        Reason = reason;
        LimitingProductId = limitingProductId;
        StartedAt = startedAt;
    }

    public Guid Id { get; private set; }
    public Guid DishId { get; private set; }
    public StopReason Reason { get; private set; }
    /// <summary>Продукт, которого не хватило (для Shortage).</summary>
    public Guid? LimitingProductId { get; private set; }
    public DateTimeOffset StartedAt { get; private set; }
    public DateTimeOffset? EndedAt { get; private set; }

    public bool IsOpen => EndedAt is null;

    public void Close(DateTimeOffset now) => EndedAt ??= now;
}
