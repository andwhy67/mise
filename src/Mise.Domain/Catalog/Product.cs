using Mise.Domain.Common;

namespace Mise.Domain.Catalog;

/// <summary>Продукт из словаря. Количества — в базовой единице <see cref="Unit"/>.</summary>
public sealed class Product
{
    private readonly List<ProductSubstitute> _substitutes = [];

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
    /// <summary>Ориентировочная цена закупки за базовую единицу (₽ за г / мл / шт): подставляется в поставку и в оценку себестоимости.</summary>
    public decimal? ReferenceUnitCost { get; set; }
    /// <summary>Основной поставщик — для заявок «что заказать».</summary>
    public string? Supplier { get; set; }

    public IReadOnlyCollection<ProductSubstitute> Substitutes => _substitutes;

    /// <summary>Полностью заменяет список замен; существующие строки обновляются на месте (ключ product_id + substitute_id).</summary>
    public void SetSubstitutes(IEnumerable<(Guid SubstituteId, decimal Ratio)> substitutes)
    {
        var target = substitutes.ToDictionary(s => s.SubstituteId, s => s.Ratio);
        var created = target.Where(t => _substitutes.All(s => s.SubstituteId != t.Key))
            .Select(t => new ProductSubstitute(Id, t.Key, t.Value))
            .ToList();
        foreach (var existing in _substitutes.Where(s => target.ContainsKey(s.SubstituteId)))
            existing.ChangeRatio(target[existing.SubstituteId]);
        _substitutes.RemoveAll(s => !target.ContainsKey(s.SubstituteId));
        _substitutes.AddRange(created);
    }
}
