using FluentValidation;
using Mise.Application.Common;
using Mise.Domain.Common;

namespace Mise.Application.Catalog;

public sealed record DishIngredientDto(Guid ProductId, string ProductName, MeasureUnit Unit, decimal Quantity);

public sealed record DishDto(
    Guid Id,
    string Name,
    string? Category,
    decimal Price,
    bool IsActive,
    IReadOnlyList<DishIngredientDto> Ingredients);

public sealed record DishIngredientRequest(Guid ProductId, decimal Quantity);

public sealed record CreateDishRequest(
    string Name,
    string? Category,
    decimal Price,
    bool IsActive,
    IReadOnlyList<DishIngredientRequest> Ingredients);

public sealed record UpdateDishRequest(string Name, string? Category, decimal Price, bool IsActive);

public sealed record SetDishIngredientsRequest(IReadOnlyList<DishIngredientRequest> Ingredients);

internal static class DishRules
{
    public const int MaxIngredients = 50;

    public static void ValidateIngredients<T>(this AbstractValidator<T> validator,
        System.Linq.Expressions.Expression<Func<T, IEnumerable<DishIngredientRequest>>> selector)
    {
        validator.RuleFor(selector)
            .NotEmpty().WithMessage("В составе блюда должен быть хотя бы один продукт.")
            .Must(list => list.Count() <= MaxIngredients).WithMessage($"Не больше {MaxIngredients} продуктов в составе.")
            .Must(list => list.Select(i => i.ProductId).Distinct().Count() == list.Count())
            .WithMessage("Продукт указан в составе несколько раз.");
        validator.RuleForEach(selector).ChildRules(item =>
        {
            item.RuleFor(i => i.ProductId).NotEmpty();
            item.RuleFor(i => i.Quantity).IsQuantity();
        });
    }

    public static void ValidateDishFields<T>(this AbstractValidator<T> validator,
        System.Linq.Expressions.Expression<Func<T, string>> name,
        System.Linq.Expressions.Expression<Func<T, string?>> category,
        System.Linq.Expressions.Expression<Func<T, decimal>> price)
    {
        validator.RuleFor(name).NotEmpty().MaximumLength(Limits.NameMaxLength);
        validator.RuleFor(category).MaximumLength(100);
        validator.RuleFor(price).GreaterThanOrEqualTo(0m).LessThan(10_000_000m)
            .Must(p => decimal.Round(p, 2) == p).WithMessage("Цена — не больше двух знаков после запятой.");
    }
}

internal sealed class CreateDishRequestValidator : AbstractValidator<CreateDishRequest>
{
    public CreateDishRequestValidator()
    {
        this.ValidateDishFields(x => x.Name, x => x.Category, x => x.Price);
        this.ValidateIngredients(x => x.Ingredients);
    }
}

internal sealed class UpdateDishRequestValidator : AbstractValidator<UpdateDishRequest>
{
    public UpdateDishRequestValidator() => this.ValidateDishFields(x => x.Name, x => x.Category, x => x.Price);
}

internal sealed class SetDishIngredientsRequestValidator : AbstractValidator<SetDishIngredientsRequest>
{
    public SetDishIngredientsRequestValidator() => this.ValidateIngredients(x => x.Ingredients);
}
