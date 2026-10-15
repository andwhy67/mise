namespace Mise.Domain.Supplies;

/// <summary>Поставка: дата создания, уникальный идентификатор, номер, список продуктов.</summary>
public sealed class Supply
{
    private readonly List<SupplyItem> _items = [];

    private Supply() { }

    public Supply(Guid id, string number, DateTimeOffset createdAt, DateTimeOffset registeredAt)
    {
        Id = id;
        Number = number;
        CreatedAt = createdAt.ToUniversalTime();
        RegisteredAt = registeredAt;
    }

    public Guid Id { get; private set; }
    public string Number { get; private set; } = string.Empty;
    /// <summary>Дата создания поставки — от неё отсчитываются сроки хранения позиций.</summary>
    public DateTimeOffset CreatedAt { get; private set; }
    /// <summary>Когда поставка зарегистрирована в системе (по IClock).</summary>
    public DateTimeOffset RegisteredAt { get; private set; }
    public string? SupplierName { get; set; }
    public string? IdempotencyKey { get; set; }
    /// <summary>Хэш тела запроса — отличает честный повтор от повторного использования ключа.</summary>
    public string? RequestHash { get; set; }

    public IReadOnlyCollection<SupplyItem> Items => _items;

    public void AddItem(SupplyItem item) => _items.Add(item);
}

public sealed class SupplyItem
{
    private SupplyItem() { }

    /// <summary>Цена закупки за базовую единицу (₽ за г / мл / шт) — та же, что у партии.</summary>
    public decimal? UnitCost { get; set; }

    public SupplyItem(Guid id, Guid supplyId, int lineNo, Guid productId, string productName, decimal quantity)
    {
        Id = id;
        SupplyId = supplyId;
        LineNo = lineNo;
        ProductId = productId;
        ProductName = productName;
        Quantity = quantity;
    }

    public Guid Id { get; private set; }
    public Guid SupplyId { get; private set; }
    /// <summary>Номер строки в поставке (с 1) — сохраняет порядок позиций.</summary>
    public int LineNo { get; private set; }
    public Guid ProductId { get; private set; }
    /// <summary>Название на момент поставки (snapshot).</summary>
    public string ProductName { get; private set; } = string.Empty;
    /// <summary>Количество в базовой единице продукта.</summary>
    public decimal Quantity { get; private set; }
    /// <summary>Срок хранения из позиции (часы), если передан.</summary>
    public int? ShelfLifeHours { get; set; }
    /// <summary>Явная дата годности из позиции, если передана.</summary>
    public DateTimeOffset? ExpiresAt { get; set; }
}
