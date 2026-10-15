using FluentValidation;
using FluentValidation.Results;

namespace Mise.Application.Common;

internal static class ValidationExtensions
{
    /// <summary>Проверяет запрос и бросает <see cref="ValidationException"/> (→ 400 ValidationProblem).</summary>
    public static async Task EnsureValidAsync<T>(this IValidator<T> validator, T request, CancellationToken ct)
    {
        var result = await validator.ValidateAsync(request, ct);
        if (!result.IsValid)
            throw new ValidationException(result.Errors);
    }

    public static ValidationException ToValidationException(this IEnumerable<ValidationFailure> failures) => new(failures);

    /// <summary>Не больше 3 знаков после запятой — numeric(14,3).</summary>
    public static IRuleBuilderOptions<T, decimal> HasQuantityScale<T>(this IRuleBuilder<T, decimal> rule) =>
        rule.Must(v => decimal.Round(v, 3) == v).WithMessage("Не больше трёх знаков после запятой.");

    public static IRuleBuilderOptions<T, decimal> IsQuantity<T>(this IRuleBuilder<T, decimal> rule) =>
        rule.GreaterThan(0m).LessThanOrEqualTo(Limits.MaxQuantity).HasQuantityScale();
}

public static class Limits
{
    /// <summary>Верхняя граница количества в базовых единицах (numeric(14,3) вмещает до 10^11).</summary>
    public const decimal MaxQuantity = 1_000_000_000m;
    public const int MaxShelfLifeHours = 24 * 365 * 3;
    public const int NameMaxLength = 200;
    /// <summary>Цена закупки за кг / л / шт, ₽.</summary>
    public const decimal MaxPrice = 10_000_000m;
}
