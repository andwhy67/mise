using Mise.Domain.Common;

namespace Mise.Domain.Catalog;

/// <summary>Сколько продукта (в его базовой единице) уходит на выход рецепта заготовки <see cref="Product.PrepYield"/>.</summary>
public sealed class PrepIngredient
{
    public static class Errors
    {
        public const string Invalid = "product.invalid_prep";
    }

    private PrepIngredient() { }

    internal PrepIngredient(Guid productId, Guid ingredientId, decimal quantity)
    {
        ProductId = productId;
        IngredientId = ingredientId;
        Change(quantity);
    }

    public Guid ProductId { get; private set; }
    public Guid IngredientId { get; private set; }
    public decimal Quantity { get; private set; }

    internal void Change(decimal quantity)
    {
        if (quantity <= 0)
            throw new DomainException(Errors.Invalid, "Количество ингредиента должно быть больше нуля.");
        Quantity = quantity;
    }
}
