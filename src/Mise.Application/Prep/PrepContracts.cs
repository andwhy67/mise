using FluentValidation;
using Mise.Application.Common;
using Mise.Domain.Common;

namespace Mise.Application.Prep;

/// <param name="Quantity">Сколько заготовки приготовить.</param>
/// <param name="Unit">Единица ввода; по умолчанию — базовая единица заготовки.</param>
public sealed record CookPrepRequest(decimal Quantity, InputUnit? Unit = null);

/// <summary>Заготовка в списке «что можно приготовить».</summary>
/// <param name="MaxQuantity">Сколько можно приготовить из пригодных остатков сейчас (базовые единицы).</param>
public sealed record PrepItemDto(Guid Id, string Name, MeasureUnit Unit, decimal Yield, int ShelfLifeHours, decimal Usable, decimal MaxQuantity);

/// <param name="ExpiresAt">Срок будущей партии заготовки.</param>
/// <param name="LimitingProductName">Продукт, чей срок укоротил заготовку; null — заготовка живёт полный срок.</param>
/// <param name="Cost">Себестоимость всей партии, ₽; null — цена хотя бы одного продукта неизвестна.</param>
/// <param name="RescuedCost">Сколько из этого — продукты на грани срока, которые иначе ушли бы в списание, ₽.</param>
public sealed record PrepPlanDto(
    Guid PrepId,
    string PrepName,
    MeasureUnit Unit,
    decimal Quantity,
    bool CanCook,
    DateTimeOffset ExpiresAt,
    int ShelfLifeHours,
    string? LimitingProductName,
    decimal? Cost,
    decimal RescuedCost,
    IReadOnlyList<PrepIngredientUseDto> Ingredients);

public sealed record PrepIngredientUseDto(
    Guid ProductId,
    string ProductName,
    MeasureUnit Unit,
    decimal Required,
    decimal Available,
    decimal Shortage,
    IReadOnlyList<PrepBatchUseDto> Batches);

/// <param name="Rescued">Партия на грани срока: в заготовке она «спасена», а не списана.</param>
public sealed record PrepBatchUseDto(Guid BatchId, decimal Quantity, DateTimeOffset ExpiresAt, bool Rescued);

/// <param name="Number">Номер внутренней поставки «ЗАГ-…», через которую партия заготовки попала на склад.</param>
public sealed record PrepResultDto(Guid SupplyId, string Number, Guid BatchId, PrepPlanDto Plan);

internal sealed class CookPrepRequestValidator : AbstractValidator<CookPrepRequest>
{
    public CookPrepRequestValidator()
    {
        RuleFor(x => x.Quantity).IsQuantity();
        RuleFor(x => x.Unit).IsInEnum();
    }
}
