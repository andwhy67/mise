using Mise.Domain.Common;

namespace Mise.Domain.Tests.Common;

public class RuFormatTests
{
    [Theory]
    [InlineData(180, MeasureUnit.Gram, "180 г")]
    [InlineData(2000, MeasureUnit.Gram, "2 кг")]
    [InlineData(1250, MeasureUnit.Gram, "1,3 кг")]
    [InlineData(750, MeasureUnit.Milliliter, "750 мл")]
    [InlineData(3000, MeasureUnit.Milliliter, "3 л")]
    [InlineData(12, MeasureUnit.Piece, "12 шт")]
    [InlineData(0.5, MeasureUnit.Piece, "0,5 шт")]
    public void Quantity_uses_readable_units(decimal value, MeasureUnit unit, string expected) =>
        Assert.Equal(expected, RuFormat.Quantity(value, unit));

    [Theory]
    [InlineData(0.25, "15 мин")]
    [InlineData(0.001, "1 мин")]
    [InlineData(4.46, "4,5 ч")]
    [InlineData(6, "6 ч")]
    [InlineData(11.6, "12 ч")]
    public void Hours_are_short(decimal hours, string expected) => Assert.Equal(expected, RuFormat.Hours(hours));

    [Theory]
    [InlineData(1, "1 порции")]
    [InlineData(2, "2 порций")]
    [InlineData(11, "11 порций")]
    [InlineData(13, "13 порций")]
    [InlineData(21, "21 порции")]
    public void Portions_after_do_are_genitive(int count, string expected) => Assert.Equal(expected, RuFormat.PortionsGenitive(count));
}
