using FluentValidation;
using Mise.Application.Common;
using Mise.Domain.Common;
using Mise.Domain.Expiry;
using Mise.Domain.Stock;

namespace Mise.Application.Supplies;

/// <param name="Unit">Единица ввода; по умолчанию — базовая единица продукта (кг/л пересчитываются в г/мл).</param>
/// <param name="ShelfLifeHours">Срок хранения в часах от даты создания поставки.</param>
/// <param name="ExpiresAt">Явная дата годности — приоритетнее срока хранения.</param>
public sealed record CreateSupplyItemRequest(
    Guid ProductId,
    string? Name,
    decimal Quantity,
    InputUnit? Unit,
    int? ShelfLifeHours,
    DateTimeOffset? ExpiresAt,
    decimal? Price = null);

/// <param name="Id">Идентификатор поставки; если не передан — генерируется.</param>
/// <param name="CreatedAt">Дата создания поставки; по умолчанию — текущее время сервера.</param>
public sealed record CreateSupplyRequest(
    Guid? Id,
    string Number,
    DateTimeOffset? CreatedAt,
    string? Supplier,
    IReadOnlyList<CreateSupplyItemRequest> Items);

public sealed record SupplyItemDto(
    Guid Id,
    int LineNo,
    Guid ProductId,
    string ProductName,
    MeasureUnit Unit,
    decimal Quantity,
    int? ShelfLifeHours,
    DateTimeOffset? RequestedExpiresAt,
    Guid BatchId,
    DateTimeOffset ExpiresAt,
    ExpirySource ExpirySource,
    BatchStatus BatchStatus,
    decimal? Price,
    decimal? LineCost);

public sealed record SupplyDto(
    Guid Id,
    string Number,
    DateTimeOffset CreatedAt,
    DateTimeOffset RegisteredAt,
    string? Supplier,
    IReadOnlyList<SupplyItemDto> Items);

public sealed record SupplyListItemDto(
    Guid Id,
    string Number,
    DateTimeOffset CreatedAt,
    DateTimeOffset RegisteredAt,
    string? Supplier,
    int ItemCount);

/// <summary>Результат приёма: <see cref="Created"/> = false, если это идемпотентный повтор.</summary>
public sealed record CreateSupplyResult(SupplyDto Supply, bool Created);

internal sealed class CreateSupplyRequestValidator : AbstractValidator<CreateSupplyRequest>
{
    public const int MaxItems = 200;
    public const int NumberMaxLength = 64;

    public CreateSupplyRequestValidator()
    {
        RuleFor(x => x.Id).NotEqual(Guid.Empty);
        RuleFor(x => x.Number).NotEmpty().MaximumLength(NumberMaxLength)
            .Matches("^[A-Za-zА-Яа-яЁё0-9][A-Za-zА-Яа-яЁё0-9._/-]*$")
            .WithMessage("Номер поставки: буквы, цифры и символы . _ / -");
        RuleFor(x => x.Supplier).MaximumLength(Limits.NameMaxLength);
        RuleFor(x => x.Items)
            .NotEmpty().WithMessage("В поставке должен быть хотя бы один продукт.")
            .Must(items => items.Count <= MaxItems).WithMessage($"Не больше {MaxItems} позиций в поставке.");
        RuleForEach(x => x.Items).ChildRules(item =>
        {
            item.RuleFor(i => i.ProductId).NotEmpty();
            item.RuleFor(i => i.Name).MaximumLength(Limits.NameMaxLength);
            item.RuleFor(i => i.Quantity).GreaterThan(0m).LessThanOrEqualTo(Limits.MaxQuantity);
            item.RuleFor(i => i.Unit).IsInEnum();
            item.RuleFor(i => i.ShelfLifeHours).InclusiveBetween(1, Limits.MaxShelfLifeHours);
            item.RuleFor(i => i.Price).GreaterThanOrEqualTo(0m).LessThanOrEqualTo(Limits.MaxPrice);
        });
    }
}
