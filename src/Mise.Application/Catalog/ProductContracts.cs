using FluentValidation;
using Mise.Application.Common;
using Mise.Domain.Catalog;
using Mise.Domain.Common;

namespace Mise.Application.Catalog;

public sealed record ProductDto(
    Guid Id,
    string Name,
    MeasureUnit Unit,
    int? DefaultShelfLifeHours,
    decimal MinStockQty,
    decimal PackSize,
    string? Category,
    string? StorageType)
{
    public static ProductDto From(Product p) =>
        new(p.Id, p.Name, p.Unit, p.DefaultShelfLifeHours, p.MinStockQty, p.PackSize, p.Category, p.StorageType);
}

public sealed record UpsertProductRequest(
    string Name,
    MeasureUnit Unit,
    int? DefaultShelfLifeHours,
    decimal MinStockQty,
    decimal PackSize,
    string? Category,
    string? StorageType);

internal sealed class UpsertProductRequestValidator : AbstractValidator<UpsertProductRequest>
{
    public UpsertProductRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(Limits.NameMaxLength);
        RuleFor(x => x.Unit).IsInEnum();
        RuleFor(x => x.DefaultShelfLifeHours).InclusiveBetween(1, Limits.MaxShelfLifeHours);
        RuleFor(x => x.MinStockQty).GreaterThanOrEqualTo(0m).LessThanOrEqualTo(Limits.MaxQuantity).HasQuantityScale();
        RuleFor(x => x.PackSize).IsQuantity();
        RuleFor(x => x.Category).MaximumLength(100);
        RuleFor(x => x.StorageType).MaximumLength(100);
    }
}
