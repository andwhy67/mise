using FluentValidation;
using Mise.Domain.Common;
using Mise.Domain.Orders;

namespace Mise.Application.Orders;

public sealed record CreateOrderItemRequest(Guid DishId, int Quantity);

/// <summary>Повар согласился: весь <see cref="ProductId"/> заказа взять из <see cref="SubstituteId"/> (по правилу из справочника).</summary>
public sealed record OrderSubstitutionRequest(Guid ProductId, Guid SubstituteId);

public sealed record CreateOrderRequest(IReadOnlyList<CreateOrderItemRequest> Items, IReadOnlyList<OrderSubstitutionRequest>? Substitutions = null);

public sealed record ChangeOrderStatusRequest(OrderStatus Status);

public sealed record OrderItemDto(int LineNo, Guid DishId, string DishName, int Quantity, decimal Price, decimal Amount);

/// <summary>Из какой партии сколько списано (или возвращено) по заказу.</summary>
public sealed record OrderConsumptionDto(
    Guid ProductId,
    string ProductName,
    MeasureUnit Unit,
    Guid BatchId,
    DateTimeOffset BatchExpiresAt,
    decimal Consumed,
    decimal Returned);

public sealed record OrderDto(
    Guid Id,
    string Number,
    OrderStatus Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? CancelledAt,
    decimal Total,
    IReadOnlyList<OrderItemDto> Items,
    IReadOnlyList<OrderConsumptionDto> Consumption,
    IReadOnlyList<OrderSubstitutionDto> Substitutions);

public sealed record OrderSubstitutionDto(Guid ProductId, string ProductName, Guid SubstituteId, string SubstituteName, MeasureUnit Unit, decimal Ratio);

public sealed record OrderListItemDto(Guid Id, string Number, OrderStatus Status, DateTimeOffset CreatedAt, int Portions, decimal Total);

public sealed record CreateOrderResult(OrderDto Order, bool Created);

/// <summary>Чего не хватило для заказа — тело отказа 422 (<c>missing[]</c>).</summary>
public sealed record MissingIngredientDto(
    Guid ProductId,
    string ProductName,
    MeasureUnit Unit,
    decimal Required,
    decimal Available,
    decimal Shortage,
    IReadOnlyList<string> Dishes,
    IReadOnlyList<SubstituteOfferDto> Substitutes);

/// <summary>
/// Чем можно заменить недостающий продукт (правило из справочника). <see cref="Required"/> — сколько заменителя понадобится
/// всему заказу вместе с его собственным расходом; <see cref="Enough"/> — хватит ли пригодного остатка.
/// </summary>
public sealed record SubstituteOfferDto(Guid ProductId, string ProductName, MeasureUnit Unit, decimal Ratio, decimal Required, decimal Available, bool Enough);

/// <summary>Возврат при отмене: что вернулось в партии и что ушло в потерю (партия уже просрочена/списана).</summary>
public sealed record CancelledLineDto(Guid ProductId, string ProductName, MeasureUnit Unit, Guid BatchId, decimal Quantity);

public sealed record CancelOrderResult(OrderDto Order, IReadOnlyList<CancelledLineDto> Returned, IReadOnlyList<CancelledLineDto> Lost);

internal sealed class CreateOrderRequestValidator : AbstractValidator<CreateOrderRequest>
{
    public const int MaxLines = 50;
    public const int MaxPortionsPerLine = 100;

    public CreateOrderRequestValidator()
    {
        RuleFor(x => x.Items)
            .NotEmpty().WithMessage("В заказе должно быть хотя бы одно блюдо.")
            .Must(items => items.Count <= MaxLines).WithMessage($"Не больше {MaxLines} позиций в заказе.");
        RuleForEach(x => x.Items).ChildRules(item =>
        {
            item.RuleFor(i => i.DishId).NotEmpty();
            item.RuleFor(i => i.Quantity).InclusiveBetween(1, MaxPortionsPerLine);
        });
        RuleFor(x => x.Substitutions)
            .Must(s => s is null || s.Count <= MaxLines).WithMessage($"Не больше {MaxLines} замен.")
            .Must(s => s is null || s.Select(x => x.ProductId).Distinct().Count() == s.Count).WithMessage("Продукт заменяется один раз.")
            .Must(s => s is null || !s.Any(x => s.Any(y => y.ProductId == x.SubstituteId))).WithMessage("Заменитель сам не может быть заменён.");
        RuleForEach(x => x.Substitutions).ChildRules(s =>
        {
            s.RuleFor(x => x.ProductId).NotEmpty();
            s.RuleFor(x => x.SubstituteId).NotEmpty().NotEqual(x => x.ProductId).WithMessage("Продукт не может заменять сам себя.");
        });
    }
}

internal sealed class ChangeOrderStatusRequestValidator : AbstractValidator<ChangeOrderStatusRequest>
{
    public ChangeOrderStatusRequestValidator() => RuleFor(x => x.Status).IsInEnum();
}
