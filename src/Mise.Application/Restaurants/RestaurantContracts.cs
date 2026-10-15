using FluentValidation;
using Mise.Application.Auth;
using Mise.Domain.Restaurants;
using Mise.Domain.Users;

namespace Mise.Application.Restaurants;

/// <param name="FillDemoCatalog">Сразу заполнить справочник стартовыми продуктами и блюдами (с ценами, поставщиками, заменами).</param>
public sealed record RegisterRestaurantRequest(string RestaurantName, string DisplayName, string Login, string Password, bool FillDemoCatalog = false);

public sealed record JoinRestaurantRequest(string Code, string DisplayName, string Login, string Password);

/// <summary>Что покажет экран «По приглашению» до регистрации: куда и кем.</summary>
public sealed record InvitePreviewDto(string RestaurantName, UserRole Role, DateTimeOffset ExpiresAt);

public sealed record CreateInviteRequest(UserRole Role);

/// <param name="Code">Код как его диктуют: «K7Q2-PXM4».</param>
/// <param name="Status">Active, Accepted, Revoked, Expired.</param>
public sealed record InviteDto(
    Guid Id,
    string Code,
    UserRole Role,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt,
    DateTimeOffset? AcceptedAt,
    string? AcceptedBy)
{
    public static InviteDto From(Invite invite, DateTimeOffset now, string? acceptedBy) => new(
        invite.Id,
        invite.DisplayCode,
        invite.Role,
        invite.AcceptedAt is not null ? "Accepted" : invite.RevokedAt is not null ? "Revoked" : invite.IsUsableAt(now) ? "Active" : "Expired",
        invite.CreatedAt,
        invite.ExpiresAt,
        invite.AcceptedAt,
        acceptedBy);
}

/// <summary>Стартовый справочник для нового ресторана (те же продукты и блюда, что в демо, но свои).</summary>
public interface ICatalogTemplate
{
    /// <summary>Добавляет продукты и блюда в текущий ресторан (без сохранения).</summary>
    void AddTo(Abstractions.IMiseDbContext db, DateTimeOffset now);
}

internal static class AccountRules
{
    public static bool BeValidLogin(string login)
    {
        try
        {
            User.NormalizeLogin(login);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    public const string LoginMessage = "Логин: 3–32 символа, латиница, цифры, «.», «_», «-».";
}

internal sealed class RegisterRestaurantRequestValidator : AbstractValidator<RegisterRestaurantRequest>
{
    public RegisterRestaurantRequestValidator()
    {
        RuleFor(x => x.RestaurantName).NotEmpty().MaximumLength(Restaurant.NameMaxLength)
            .Must(n => n is null || n.Trim().Length > 0).WithMessage("Укажите название ресторана.");
        RuleFor(x => x.DisplayName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Login).NotEmpty().Must(AccountRules.BeValidLogin).WithMessage(AccountRules.LoginMessage);
        RuleFor(x => x.Password).IsPassword();
    }
}

internal sealed class JoinRestaurantRequestValidator : AbstractValidator<JoinRestaurantRequest>
{
    public JoinRestaurantRequestValidator()
    {
        RuleFor(x => x.Code).NotEmpty().MaximumLength(32);
        RuleFor(x => x.DisplayName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Login).NotEmpty().Must(AccountRules.BeValidLogin).WithMessage(AccountRules.LoginMessage);
        RuleFor(x => x.Password).IsPassword();
    }
}

internal sealed class CreateInviteRequestValidator : AbstractValidator<CreateInviteRequest>
{
    public CreateInviteRequestValidator() =>
        RuleFor(x => x.Role).Must(r => r is UserRole.Manager or UserRole.Warehouse or UserRole.Chef)
            .WithMessage("Пригласить можно менеджера, кладовщика или повара.");
}
