using Mise.Domain.Common;

namespace Mise.Domain.Tests.Common;

public class UnitConverterTests
{
    [Theory]
    [InlineData(1.5, InputUnit.Kilogram, MeasureUnit.Gram, 1500)]
    [InlineData(250, InputUnit.Gram, MeasureUnit.Gram, 250)]
    [InlineData(2, InputUnit.Liter, MeasureUnit.Milliliter, 2000)]
    [InlineData(330, InputUnit.Milliliter, MeasureUnit.Milliliter, 330)]
    [InlineData(12, InputUnit.Piece, MeasureUnit.Piece, 12)]
    public void Converts_input_to_product_base_unit(decimal qty, InputUnit input, MeasureUnit productUnit, decimal expected)
    {
        Assert.Equal(expected, UnitConverter.ToBase(qty, input, productUnit));
    }

    [Theory]
    [InlineData(InputUnit.Kilogram, MeasureUnit.Milliliter)]
    [InlineData(InputUnit.Liter, MeasureUnit.Gram)]
    [InlineData(InputUnit.Piece, MeasureUnit.Gram)]
    [InlineData(InputUnit.Gram, MeasureUnit.Piece)]
    public void Incompatible_units_are_rejected(InputUnit input, MeasureUnit productUnit)
    {
        var ex = Assert.Throws<DomainException>(() => UnitConverter.ToBase(1m, input, productUnit));

        Assert.Equal(UnitConverter.Errors.Incompatible, ex.Code);
    }

    [Fact]
    public void Precision_finer_than_base_unit_thousandths_is_rejected()
    {
        // 0.0000001 кг = 0.0001 г → больше 3 знаков после запятой в базовой единице.
        var ex = Assert.Throws<DomainException>(() => UnitConverter.ToBase(0.0000001m, InputUnit.Kilogram, MeasureUnit.Gram));

        Assert.Equal(UnitConverter.Errors.TooPrecise, ex.Code);
    }

    [Fact]
    public void Default_input_unit_matches_product_unit()
    {
        Assert.Equal(InputUnit.Gram, UnitConverter.DefaultInputFor(MeasureUnit.Gram));
        Assert.Equal(InputUnit.Milliliter, UnitConverter.DefaultInputFor(MeasureUnit.Milliliter));
        Assert.Equal(InputUnit.Piece, UnitConverter.DefaultInputFor(MeasureUnit.Piece));
    }
}
