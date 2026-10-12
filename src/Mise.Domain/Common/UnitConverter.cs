namespace Mise.Domain.Common;

public static class UnitConverter
{
    /// <summary>Точность хранения количеств: numeric(14,3).</summary>
    public const int Scale = 3;

    public static class Errors
    {
        public const string Incompatible = "unit.incompatible";
        public const string TooPrecise = "unit.too_precise";
    }

    public static InputUnit DefaultInputFor(MeasureUnit unit) => unit switch
    {
        MeasureUnit.Gram => InputUnit.Gram,
        MeasureUnit.Milliliter => InputUnit.Milliliter,
        MeasureUnit.Piece => InputUnit.Piece,
        _ => throw new ArgumentOutOfRangeException(nameof(unit), unit, null),
    };

    /// <summary>Переводит количество во входной единице в базовую единицу продукта.</summary>
    public static decimal ToBase(decimal quantity, InputUnit input, MeasureUnit productUnit)
    {
        var factor = (input, productUnit) switch
        {
            (InputUnit.Gram, MeasureUnit.Gram) => 1m,
            (InputUnit.Kilogram, MeasureUnit.Gram) => 1000m,
            (InputUnit.Milliliter, MeasureUnit.Milliliter) => 1m,
            (InputUnit.Liter, MeasureUnit.Milliliter) => 1000m,
            (InputUnit.Piece, MeasureUnit.Piece) => 1m,
            _ => throw new DomainException(Errors.Incompatible,
                $"Единица «{input}» несовместима с единицей продукта «{productUnit}»."),
        };

        var result = quantity * factor;
        if (decimal.Round(result, Scale) != result)
            throw new DomainException(Errors.TooPrecise, $"Количество {result} точнее 0,001 базовой единицы.");
        return result;
    }
}
