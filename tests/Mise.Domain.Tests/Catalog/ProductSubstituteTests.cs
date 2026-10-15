using Mise.Domain.Catalog;
using Mise.Domain.Common;

namespace Mise.Domain.Tests.Catalog;

public class ProductSubstituteTests
{
    private static readonly Guid Arugula = Guid.NewGuid();
    private static readonly Guid Romaine = Guid.NewGuid();

    private static Product Spinach() => new(Guid.NewGuid(), "Шпинат", MeasureUnit.Gram);

    [Fact]
    public void SetSubstitutes_replaces_the_list_keeping_existing_rows()
    {
        var spinach = Spinach();
        spinach.SetSubstitutes([(Arugula, 1m)]);
        var row = spinach.Substitutes.Single();

        spinach.SetSubstitutes([(Arugula, 0.8m), (Romaine, 1.2m)]);

        Assert.Equal(2, spinach.Substitutes.Count);
        Assert.Same(row, spinach.Substitutes.Single(s => s.SubstituteId == Arugula));
        Assert.Equal(0.8m, row.Ratio);
        Assert.All(spinach.Substitutes, s => Assert.Equal(spinach.Id, s.ProductId));
    }

    [Fact]
    public void Product_cannot_substitute_itself()
    {
        var spinach = Spinach();

        var error = Assert.Throws<DomainException>(() => spinach.SetSubstitutes([(spinach.Id, 1m)]));
        Assert.Equal(ProductSubstitute.Errors.Invalid, error.Code);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(11)]
    public void Ratio_must_be_positive_and_sane(decimal ratio)
    {
        Assert.Throws<DomainException>(() => Spinach().SetSubstitutes([(Arugula, ratio)]));
    }
}
