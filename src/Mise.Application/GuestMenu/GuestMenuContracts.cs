using FluentValidation;
using Mise.Application.Orders;
using Mise.Domain.Orders;

namespace Mise.Application.GuestMenu;

/// <summary>Можно ли заказать блюдо: как в меню, только с изменениями (что-то закончилось) или нельзя.</summary>
public enum GuestDishState
{
    Available,
    WithChanges,
    Unavailable,
}

/// <summary>
/// Меню для гостя: без остатков в граммах и без порций — только «есть / можно с изменениями / нет».
/// Склад ресторана наружу не показывается.
/// </summary>
/// <param name="Ordering">Гости могут заказывать (иначе меню только для просмотра).</param>
public sealed record GuestMenuDto(string Restaurant, bool Ordering, int Tables, int MaxPortions, IReadOnlyList<GuestDishDto> Dishes);

/// <param name="Recommended">«Шеф рекомендует» — из горячих предложений.</param>
/// <param name="SuggestedChanges">Для <see cref="GuestDishState.WithChanges"/>: с какими изменениями блюдо можно подать.</param>
public sealed record GuestDishDto(
    Guid Id,
    string Name,
    string? Category,
    decimal Price,
    GuestDishState State,
    bool Recommended,
    IReadOnlyList<GuestIngredientDto> Ingredients,
    IReadOnlyList<GuestChangeDto> SuggestedChanges);

/// <param name="Out">Закончился — без замены или удаления блюдо не приготовить.</param>
/// <param name="Substitutes">Чем можно заменить — только те заменители, которых хватит на порцию.</param>
public sealed record GuestIngredientDto(Guid ProductId, string Name, bool Removable, bool Out, IReadOnlyList<GuestSubstituteDto> Substitutes);

public sealed record GuestSubstituteDto(Guid ProductId, string Name);

public sealed record GuestChangeDto(Guid ProductId, ModificationKind Kind, Guid? SubstituteId);

/// <param name="Table">Номер стола из QR стола; null — общий QR.</param>
public sealed record PlaceGuestOrderRequest(IReadOnlyList<CreateOrderItemRequest> Items, int? Table, PaymentMethod Payment);

/// <summary>Заказ глазами гостя: номер, статус и состав — по секретной ссылке, без входа.</summary>
public sealed record GuestOrderDto(
    string Token,
    string Number,
    OrderStatus Status,
    DateTimeOffset CreatedAt,
    int? Table,
    PaymentMethod Payment,
    decimal Total,
    IReadOnlyList<GuestOrderItemDto> Items);

public sealed record GuestOrderItemDto(string DishName, int Quantity, decimal Amount, IReadOnlyList<OrderItemModificationDto> Modifications);

/// <summary>Настройки QR-меню для менеджера.</summary>
public sealed record GuestMenuSettingsDto(string MenuKey, bool Ordering, int Tables);

public sealed record UpdateGuestMenuRequest(bool Ordering, int Tables);

internal sealed class PlaceGuestOrderRequestValidator : AbstractValidator<PlaceGuestOrderRequest>
{
    public PlaceGuestOrderRequestValidator()
    {
        RuleFor(x => x.Items).NotEmpty().WithMessage("Добавьте хотя бы одно блюдо.")
            .Must(items => items.Sum(i => i.Quantity) <= GuestMenuService.MaxPortions)
            .WithMessage($"Не больше {GuestMenuService.MaxPortions} порций в одном заказе — для большего заказа позовите официанта.");
        RuleFor(x => x.Table).GreaterThan(0).When(x => x.Table is not null);
        RuleFor(x => x.Payment).IsInEnum();
    }
}

internal sealed class UpdateGuestMenuRequestValidator : AbstractValidator<UpdateGuestMenuRequest>
{
    public UpdateGuestMenuRequestValidator() =>
        RuleFor(x => x.Tables).InclusiveBetween(0, Domain.Restaurants.Restaurant.MaxTables);
}
