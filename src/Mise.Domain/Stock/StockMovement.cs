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

    public static StockMovement Receipt(Guid id, Batch batch, Guid supplyId, DateTimeOffset now) => new()
    {
        Id = id,
        BatchId = batch.Id,
        ProductId = batch.ProductId,
        Type = MovementType.Receipt,
        QtyDelta = batch.InitialQty,
        SupplyId = supplyId,
        CreatedAt = now,
    };

    public static StockMovement OrderConsumption(Guid id, Batch batch, decimal quantity, Guid orderId, DateTimeOffset now) => new()
    {
        Id = id,
        BatchId = batch.Id,
        ProductId = batch.ProductId,
        Type = MovementType.OrderConsumption,
        QtyDelta = -quantity,
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
        OrderId = orderId,
        Reason = "Отмена заказа",
        CreatedAt = now,
    };
}
