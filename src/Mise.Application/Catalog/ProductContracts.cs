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
    string? StorageType,
    decimal? ReferencePrice,
    string? Supplier,
    IReadOnlyList<ProductSubstituteDto> Substitutes,
    PrepRecipeDto? Prep = null)
{
    /// <param name="p">Цена отдаётся за кг, л или штуку — как её называют на кухне.</param>
    /// <param name="names">Названия и единицы продуктов-заменителей.</param>
    public static ProductDto From(Product p, IReadOnlyDictionary<Guid, (string Name, MeasureUnit Unit)> names) =>
        new(p.Id, p.Name, p.Unit, p.DefaultShelfLifeHours, p.MinStockQty, p.PackSize, p.Category, p.StorageType,
            p.ReferenceUnitCost is { } cost ? Prices.ToDisplay(cost, p.Unit) : null, p.Supplier,
            p.Substitutes
                .Where(s => names.ContainsKey(s.SubstituteId))
                .Select(s => new ProductSubstituteDto(s.SubstituteId, names[s.SubstituteId].Name, names[s.SubstituteId].Unit, s.Ratio))
                .OrderBy(s => s.ProductName)
                .ToList(),
            p.PrepYield is { } yield
                ? new PrepRecipeDto(yield, p.PrepIngredients
                    .Where(i => names.ContainsKey(i.IngredientId))
                    .Select(i => new PrepIngredientDto(i.IngredientId, names[i.IngredientId].Name, names[i.IngredientId].Unit, i.Quantity))
                    .OrderBy(i => i.ProductName)
                    .ToList())
                : null);
}

/// <param name="Yield">Выход рецепта в базовой единице заготовки: состав указан на это количество.</param>
public sealed record PrepRecipeDto(decimal Yield, IReadOnlyList<PrepIngredientDto> Ingredients);

public sealed record PrepIngredientDto(Guid ProductId, string ProductName, MeasureUnit Unit, decimal Quantity);

public sealed record PrepIngredientRequest(Guid ProductId, decimal Quantity);

/// <summary>Рецепт заготовки; пустой список ингредиентов — продукт снова закупается, а не готовится.</summary>
public sealed record SetPrepRecipeRequest(decimal Yield, IReadOnlyList<PrepIngredientRequest> Ingredients);

internal sealed class SetPrepRecipeRequestValidator : AbstractValidator<SetPrepRecipeRequest>
{
    public const int MaxIngredients = 20;

    public SetPrepRecipeRequestValidator()
    {
        RuleFor(x => x.Yield).IsQuantity();
        RuleFor(x => x.Ingredients).NotNull()
            .Must(i => i.Count <= MaxIngredients).WithMessage($"Не больше {MaxIngredients} ингредиентов.")
            .Must(i => i.Select(x => x.ProductId).Distinct().Count() == i.Count).WithMessage("Ингредиент указан дважды.");
        RuleForEach(x => x.Ingredients).ChildRules(i =>
        {
            i.RuleFor(x => x.ProductId).NotEmpty();
            i.RuleFor(x => x.Quantity).IsQuantity();
        });
    }
}

/// <param name="Ratio">Сколько заменителя (в его базовой единице) идёт вместо 1 единицы продукта.</param>
public sealed record ProductSubstituteDto(Guid ProductId, string ProductName, MeasureUnit Unit, decimal Ratio);

public sealed record ProductSubstituteRequest(Guid SubstituteId, decimal Ratio);

public sealed record UpsertProductRequest(
    string Name,
    MeasureUnit Unit,
    int? DefaultShelfLifeHours,
    decimal MinStockQty,
    decimal PackSize,
    string? Category,
    string? StorageType,
    decimal? ReferencePrice = null,
    string? Supplier = null,
    /// <summary>null — замены не меняются (клиенты, которые о них не знают); пустой список — убрать все.</summary>
    IReadOnlyList<ProductSubstituteRequest>? Substitutes = null);

internal sealed class UpsertProductRequestValidator : AbstractValidator<UpsertProductRequest>
{
    public const int MaxSubstitutes = 10;

    public UpsertProductRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(Limits.NameMaxLength);
        RuleFor(x => x.Unit).IsInEnum();
        RuleFor(x => x.DefaultShelfLifeHours).InclusiveBetween(1, Limits.MaxShelfLifeHours);
        RuleFor(x => x.MinStockQty).GreaterThanOrEqualTo(0m).LessThanOrEqualTo(Limits.MaxQuantity).HasQuantityScale();
        RuleFor(x => x.PackSize).IsQuantity();
        RuleFor(x => x.Category).MaximumLength(100);
        RuleFor(x => x.ReferencePrice).GreaterThanOrEqualTo(0m).LessThanOrEqualTo(Limits.MaxPrice);
        RuleFor(x => x.Supplier).MaximumLength(Limits.NameMaxLength);
        RuleFor(x => x.StorageType).MaximumLength(100);
        RuleFor(x => x.Substitutes)
            .Must(s => s is null || s.Count <= MaxSubstitutes).WithMessage($"Не больше {MaxSubstitutes} замен.")
            .Must(s => s is null || s.Select(x => x.SubstituteId).Distinct().Count() == s.Count).WithMessage("Заменитель указан дважды.");
        RuleForEach(x => x.Substitutes).ChildRules(s =>
        {
            s.RuleFor(x => x.SubstituteId).NotEmpty();
            s.RuleFor(x => x.Ratio).GreaterThan(0m).LessThanOrEqualTo(ProductSubstitute.MaxRatio).PrecisionScale(8, 4, ignoreTrailingZeros: true);
        });
    }
}
