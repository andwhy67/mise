namespace Mise.Domain.Stock;

public enum MovementType
{
    Receipt,
    OrderConsumption,
    ExpiryWriteOff,
    ManualWriteOff,
    OrderCancelReturn,
    Adjustment,
}

/// <summary>Неизменяемая запись журнала движений — единственный источник истины об изменении остатков.</summary>
public sealed class StockMovement
{
    private StockMovement() { }

    public Guid Id { get; private set; }
    public Guid BatchId { get; private set; }
    public Guid ProductId { get; private set; }
    public MovementType Type { get; private set; }
    /// <summary>Изменение остатка партии (+ приход, − расход).</summary>
    public decimal QtyDelta { get; private set; }
    public Guid? SupplyId { get; private set; }
    public Guid? OrderId { get; private set; }
    public string? Reason { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    /// <summary>Стоимость движения по цене партии на момент операции, ₽, со знаком количества; null — цена неизвестна.</summary>
    public decimal? Cost { get; private set; }
    /// <summary>
    /// Расход в заказ из партии, которой оставалось не больше окна «спасения» (по умолчанию 48 ч):
    /// продукт продан, а не списан. Фиксируется в момент операции.
    /// </summary>
    public bool Rescued { get; private set; }

    public static StockMovement Receipt(Guid id, Batch batch, Guid supplyId, DateTimeOffset now) => new()
    {
        Id = id,
        BatchId = batch.Id,
        ProductId = batch.ProductId,
        Type = MovementType.Receipt,
        QtyDelta = batch.InitialQty,
        Cost = batch.CostOf(batch.InitialQty),
        SupplyId = supplyId,
        CreatedAt = now,
    };

    public static StockMovement OrderConsumption(Guid id, Batch batch, decimal quantity, Guid orderId, DateTimeOffset now, RescuePolicy rescue) => new()
    {
        Id = id,
        BatchId = batch.Id,
        ProductId = batch.ProductId,
        Type = MovementType.OrderConsumption,
        QtyDelta = -quantity,
        Cost = -batch.CostOf(quantity),
        Rescued = rescue.Applies(batch, now),
        OrderId = orderId,
        CreatedAt = now,
    };

    public static StockMovement OrderCancelReturn(Guid id, Batch batch, decimal quantity, Guid orderId, DateTimeOffset now) => new()
    {
        Id = id,
        BatchId = batch.Id,
        ProductId = batch.ProductId,
        Type = MovementType.OrderCancelReturn,
        QtyDelta = quantity,
        Cost = batch.CostOf(quantity),
        OrderId = orderId,
        Reason = "Отмена заказа",
        CreatedAt = now,
    };

    public const string ExpiryWriteOffReason = "Истёк срок годности";

    public static StockMovement ExpiryWriteOff(Guid id, Batch batch, decimal quantity, DateTimeOffset now) => new()
    {
        Id = id,
        BatchId = batch.Id,
        ProductId = batch.ProductId,
        Type = MovementType.ExpiryWriteOff,
        QtyDelta = -quantity,
        Cost = -batch.CostOf(quantity),
        Reason = ExpiryWriteOffReason,
        CreatedAt = now,
    };

    public static StockMovement ManualWriteOff(Guid id, Batch batch, decimal quantity, string reason, DateTimeOffset now) => new()
    {
        Id = id,
        BatchId = batch.Id,
        ProductId = batch.ProductId,
        Type = MovementType.ManualWriteOff,
        QtyDelta = -quantity,
        Cost = -batch.CostOf(quantity),
        Reason = reason,
        CreatedAt = now,
    };
}

/// <summary>
/// Когда продажа «спасает» продукт: до конца срока осталось не больше <see cref="Window"/> и не больше
/// <see cref="ShareOfShelfLife"/> всего срока партии. Только по часам продукт с нормой в двое суток
/// считался бы спасённым всегда — даже в день привоза.
/// </summary>
public sealed record RescuePolicy(TimeSpan Window, decimal ShareOfShelfLife)
{
    public bool Applies(Batch batch, DateTimeOffset now)
    {
        var left = batch.ExpiresAt - now;
        var total = batch.ExpiresAt - batch.ReceivedAt;
        return left <= Window && total > TimeSpan.Zero && left.Ticks <= total.Ticks * ShareOfShelfLife;
    }
}
