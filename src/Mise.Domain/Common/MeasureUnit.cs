namespace Mise.Domain.Common;

/// <summary>Базовая единица хранения продукта.</summary>
public enum MeasureUnit
{
    Gram,
    Milliliter,
    Piece,
}

/// <summary>Единица, в которой количество может прийти на вход (кг/л пересчитываются в г/мл).</summary>
public enum InputUnit
{
    Gram,
    Kilogram,
    Milliliter,
    Liter,
    Piece,
}
