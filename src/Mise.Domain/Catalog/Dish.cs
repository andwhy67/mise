namespace Mise.Domain.Catalog;

public sealed class Dish
{
    private readonly List<DishIngredient> _ingredients = [];

    private Dish() { }

    public Dish(Guid id, string name)
    {
        Id = id;
        Name = name;
    }

    public Guid Id { get; private set; }
    public string Name { get; set; } = string.Empty;
    public string? Category { get; set; }
    public decimal Price { get; set; }
    /// <summary>Ручное отключение блюда (источник стоп-листа наравне с нехваткой).</summary>
    public bool IsActive { get; set; } = true;

    public IReadOnlyCollection<DishIngredient> Ingredients => _ingredients;

    /// <summary>
    /// Полностью заменяет состав блюда. Существующие строки обновляются на месте (а не удаляются и
    /// создаются заново), чтобы не было двух экземпляров с одинаковым ключом (dish_id, product_id).
    /// </summary>
    public void SetIngredients(IEnumerable<(Guid ProductId, decimal Quantity)> ingredients) =>
        SetIngredients(ingredients.Select(i => (i.ProductId, i.Quantity, Removable: false)));

    /// <param name="ingredients">Removable — гость или повар может попросить «без этого» (гарнир, посыпка, соус).</param>
    public void SetIngredients(IEnumerable<(Guid ProductId, decimal Quantity, bool Removable)> ingredients)
    {
        var target = ingredients.ToDictionary(i => i.ProductId, i => (i.Quantity, i.Removable));
        _ingredients.RemoveAll(i => !target.ContainsKey(i.ProductId));
        foreach (var existing in _ingredients)
            existing.Change(target[existing.ProductId].Quantity, target[existing.ProductId].Removable);
        var present = _ingredients.Select(i => i.ProductId).ToHashSet();
        _ingredients.AddRange(target.Where(t => !present.Contains(t.Key)).Select(t => new DishIngredient(Id, t.Key, t.Value.Quantity, t.Value.Removable)));
    }
}

/// <summary>Количество продукта (в базовых единицах) на одну порцию блюда.</summary>
public sealed class DishIngredient
{
    private DishIngredient() { }

    public DishIngredient(Guid dishId, Guid productId, decimal quantity, bool removable = false)
    {
        DishId = dishId;
        ProductId = productId;
        Quantity = quantity;
        Removable = removable;
    }

    public Guid DishId { get; private set; }
    public Guid ProductId { get; private set; }
    public decimal Quantity { get; private set; }
    /// <summary>Можно убрать из блюда по просьбе гостя или когда продукт закончился (гарнир, посыпка).</summary>
    public bool Removable { get; private set; }

    internal void Change(decimal quantity, bool removable)
    {
        Quantity = quantity;
        Removable = removable;
    }
}
