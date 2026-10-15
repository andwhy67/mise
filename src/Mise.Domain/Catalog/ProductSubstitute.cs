using Mise.Domain.Common;

namespace Mise.Domain.Catalog;

/// <summary>
/// Чем можно заменить продукт, если его не хватает: <see cref="Ratio"/> единиц замены (в её базовой единице) на 1 единицу
/// исходного. Замена — только с согласия повара в конкретном заказе; в доступность меню и стоп-лист она не входит.
/// </summary>
public sealed class ProductSubstitute
{
    public static class Errors
    {
        public const string Invalid = "product.invalid_substitute";
    }

    public const decimal MaxRatio = 10m;

    private ProductSubstitute() { }

    internal ProductSubstitute(Guid productId, Guid substituteId, decimal ratio)
    {
        if (substituteId == productId)
            throw new DomainException(Errors.Invalid, "Продукт не может заменять сам себя.");
        ProductId = productId;
        SubstituteId = substituteId;
        ChangeRatio(ratio);
    }

    public Guid ProductId { get; private set; }
    public Guid SubstituteId { get; private set; }
    public decimal Ratio { get; private set; }

    internal void ChangeRatio(decimal ratio)
    {
        if (ratio <= 0 || ratio > MaxRatio)
            throw new DomainException(Errors.Invalid, $"Коэффициент замены — больше 0 и не больше {MaxRatio}.");
        Ratio = ratio;
    }
}
