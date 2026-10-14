namespace Mise.Domain.Orders;

/// <summary>
/// Отказ в заказе из-за продукта (422 «не хватает»): чего и сколько не хватило. Заказа нет и списаний нет —
/// но факт отказа нужен аналитике «чего чаще всего не хватает» (упущенные продажи).
/// </summary>
public sealed class OrderShortage
{
    private OrderShortage() { }

    public OrderShortage(Guid id, Guid productId, decimal required, decimal available, DateTimeOffset createdAt)
    {
        Id = id;
        ProductId = productId;
        Required = required;
        Available = available;
        Shortage = required - available;
        CreatedAt = createdAt;
    }

    public Guid Id { get; private set; }
    public Guid ProductId { get; private set; }
    /// <summary>Сколько нужно было на весь заказ (базовые единицы продукта).</summary>
    public decimal Required { get; private set; }
    public decimal Available { get; private set; }
    public decimal Shortage { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
}
