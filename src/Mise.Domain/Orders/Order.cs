using Mise.Domain.Common;

namespace Mise.Domain.Orders;

public enum OrderStatus
{
    Created,
    Cooking,
    Done,
    /// <summary>Выдан гостю/официанту — заказ закрыт.</summary>
    Served,
    Cancelled,
}

/// <summary>Заказ из одного или нескольких блюд. Списание продуктов происходит при создании.</summary>
public sealed class Order
{
    public static class Errors
    {
        public const string InvalidTransition = "order.invalid_transition";
    }

    private readonly List<OrderItem> _items = [];
    private readonly List<OrderSubstitution> _substitutions = [];

    private Order() { }

    public Order(Guid id, string number, DateTimeOffset createdAt)
    {
        Id = id;
        Number = number;
        CreatedAt = createdAt;
        UpdatedAt = createdAt;
        Status = OrderStatus.Created;
    }

    public Guid Id { get; private set; }
    public string Number { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public DateTimeOffset? CancelledAt { get; private set; }
    public OrderStatus Status { get; private set; }
    public string? IdempotencyKey { get; set; }
    public string? RequestHash { get; set; }

    public IReadOnlyCollection<OrderItem> Items => _items;

    /// <summary>Замены продуктов, на которые согласился повар при оформлении заказа.</summary>
    public IReadOnlyCollection<OrderSubstitution> Substitutions => _substitutions;

    public decimal Total => _items.Sum(i => i.PriceSnapshot * i.Quantity);

    public bool CanBeCancelled => Status is OrderStatus.Created or OrderStatus.Cooking;

    public void AddItem(Guid dishId, string dishName, int quantity, decimal price) =>
        _items.Add(new OrderItem(Id, _items.Count + 1, dishId, dishName, quantity, price));

    public void AddSubstitution(Substitution substitution) =>
        _substitutions.Add(new OrderSubstitution(Id, substitution.ProductId, substitution.SubstituteId, substitution.Ratio));

    /// <summary>Только вперёд: Created → Cooking → Done → Served (Done можно и сразу из Created). Повтор текущего статуса — no-op.</summary>
    public void ChangeStatus(OrderStatus target, DateTimeOffset now)
    {
        if (target == Status)
            return;

        var allowed = (Status, target) switch
        {
            (OrderStatus.Created, OrderStatus.Cooking) => true,
            (OrderStatus.Created, OrderStatus.Done) => true,
            (OrderStatus.Cooking, OrderStatus.Done) => true,
            (OrderStatus.Done, OrderStatus.Served) => true,
            _ => false,
        };
        if (!allowed)
            throw new DomainException(Errors.InvalidTransition,
                target == OrderStatus.Cancelled
                    ? "Для отмены заказа используйте отдельную операцию отмены."
                    : $"Нельзя перевести заказ из статуса {Status} в {target}.");

        Status = target;
        UpdatedAt = now;
    }

    public void Cancel(DateTimeOffset now)
    {
        if (!CanBeCancelled)
            throw new DomainException(Errors.InvalidTransition, $"Заказ в статусе {Status} отменить нельзя.");
        Status = OrderStatus.Cancelled;
        CancelledAt = now;
        UpdatedAt = now;
    }
}

public sealed class OrderItem
{
    private OrderItem() { }

    internal OrderItem(Guid orderId, int lineNo, Guid dishId, string dishName, int quantity, decimal priceSnapshot)
    {
        OrderId = orderId;
        LineNo = lineNo;
        DishId = dishId;
        DishName = dishName;
        Quantity = quantity;
        PriceSnapshot = priceSnapshot;
    }

    public Guid OrderId { get; private set; }
    public int LineNo { get; private set; }
    public Guid DishId { get; private set; }
    /// <summary>Название блюда на момент заказа (snapshot).</summary>
    public string DishName { get; private set; } = string.Empty;
    public int Quantity { get; private set; }
    /// <summary>Цена порции на момент заказа.</summary>
    public decimal PriceSnapshot { get; private set; }
}

/// <summary>Продукт <see cref="ProductId"/> в этом заказе взят из <see cref="SubstituteId"/> (× <see cref="Ratio"/>).</summary>
public sealed class OrderSubstitution
{
    private OrderSubstitution() { }

    internal OrderSubstitution(Guid orderId, Guid productId, Guid substituteId, decimal ratio)
    {
        OrderId = orderId;
        ProductId = productId;
        SubstituteId = substituteId;
        Ratio = ratio;
    }

    public Guid OrderId { get; private set; }
    public Guid ProductId { get; private set; }
    public Guid SubstituteId { get; private set; }
    public decimal Ratio { get; private set; }
}
