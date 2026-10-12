using Mise.Domain.Common;

namespace Mise.Domain.Catalog;

/// <summary>Продукт из словаря. Количества — в базовой единице <see cref="Unit"/>.</summary>
public sealed class Product
{
    private Product() { }

    public Product(Guid id, string name, MeasureUnit unit)
    {
        Id = id;
        Name = name;
        Unit = unit;
    }

    public Guid Id { get; private set; }
    public string Name { get; set; } = string.Empty;
    public MeasureUnit Unit { get; set; }
    /// <summary>Срок хранения по умолчанию (часы), если в поставке нет ни даты, ни срока.</summary>
    public int? DefaultShelfLifeHours { get; set; }
    /// <summary>Порог дефицита (в базовых единицах).</summary>
    public decimal MinStockQty { get; set; }
    /// <summary>Кратность заказа поставщику (в базовых единицах).</summary>
    public decimal PackSize { get; set; } = 1m;
    public string? Category { get; set; }
    public string? StorageType { get; set; }
}
