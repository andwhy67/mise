using Mise.Domain.Common;

namespace Mise.Domain.Catalog;

/// <summary>Продукт из словаря. Количества — в базовой единице <see cref="Unit"/>.</summary>
public sealed class Product
{
    private readonly List<ProductSubstitute> _substitutes = [];
    private readonly List<PrepIngredient> _prepIngredients = [];

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

    /// <summary>Выход рецепта заготовки (в базовой единице): на столько соуса рассчитан состав. null — продукт закупается, а не готовится.</summary>
    public decimal? PrepYield { get; private set; }

    /// <summary>Заготовка: готовится на кухне из других продуктов (соус, бульон), срок — <see cref="DefaultShelfLifeHours"/> от приготовления.</summary>
    public bool IsPrep => PrepYield is not null;

    public IReadOnlyCollection<PrepIngredient> PrepIngredients => _prepIngredients;

    /// <summary>
    /// Рецепт заготовки: <paramref name="ingredients"/> — сколько продукта (в его базовой единице) уходит на <paramref name="yield"/> заготовки.
    /// Пустой список снимает рецепт. Заготовка не может состоять из заготовки и из самой себя; срок хранения обязателен.
    /// </summary>
    public void SetPrepRecipe(decimal yield, IEnumerable<(Guid IngredientId, decimal Quantity)> ingredients, IReadOnlySet<Guid>? prepIds = null)
    {
        var target = ingredients.ToList();
        if (target.Count == 0)
        {
            PrepYield = null;
            _prepIngredients.Clear();
            return;
        }

        if (yield <= 0)
            throw new DomainException(PrepIngredient.Errors.Invalid, "Выход заготовки должен быть больше нуля.");
        if (DefaultShelfLifeHours is null)
            throw new DomainException(PrepIngredient.Errors.Invalid, "У заготовки должен быть срок хранения.");
        if (target.Select(i => i.IngredientId).Distinct().Count() != target.Count)
            throw new DomainException(PrepIngredient.Errors.Invalid, "Ингредиент указан дважды.");
        if (target.Any(i => i.IngredientId == Id || prepIds?.Contains(i.IngredientId) == true))
            throw new DomainException(PrepIngredient.Errors.Invalid, "Заготовка готовится из продуктов, а не из заготовок.");

        PrepYield = yield;
        var map = target.ToDictionary(i => i.IngredientId, i => i.Quantity);
        _prepIngredients.RemoveAll(i => !map.ContainsKey(i.IngredientId));
        foreach (var existing in _prepIngredients)
            existing.Change(map[existing.IngredientId]);
        var present = _prepIngredients.Select(i => i.IngredientId).ToHashSet();
        _prepIngredients.AddRange(map.Where(m => !present.Contains(m.Key)).Select(m => new PrepIngredient(Id, m.Key, m.Value)));
    }
}
