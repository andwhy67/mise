using FluentValidation;
using Mise.Domain.Users;

namespace Mise.Application.Auth;

/// <summary>Настройки аутентификации (секция "Auth", PLAN §6). Секреты — только из env.</summary>
public sealed class AuthOptions
{
    public const string SectionName = "Auth";
    public const int MinSigningKeyBytes = 32;

    /// <summary>Ключ подписи JWT (HS256), не короче 32 байт. В Production обязателен из env (Auth__SigningKey).</summary>
    public string SigningKey { get; set; } = string.Empty;
    public string Issuer { get; set; } = "mise";
    public string Audience { get; set; } = "mise";
    public int AccessTokenMinutes { get; set; } = 15;
    public int RefreshTokenDays { get; set; } = 7;
    public int MaxFailedAttempts { get; set; } = 5;
    public int LockoutMinutes { get; set; } = 5;

    /// <summary>Флаг Secure у cookie с refresh-токеном (выключать только для http-разработки).</summary>
    public bool CookieSecure { get; set; } = true;

    /// <summary>Пароль демо-пользователей manager / chef / warehouse (создаются при старте, если их нет). Пусто — не создавать.</summary>
    public string? DemoUsersPassword { get; set; }

    /// <summary>Пароль служебной учётной записи simulator. Пусто — не создавать.</summary>
    public string? SimulatorPassword { get; set; }

    public LockoutPolicy Lockout => new(MaxFailedAttempts, TimeSpan.FromMinutes(LockoutMinutes));

    public bool IsValid() =>
        System.Text.Encoding.UTF8.GetByteCount(SigningKey) >= MinSigningKeyBytes
        && !string.IsNullOrWhiteSpace(Issuer) && !string.IsNullOrWhiteSpace(Audience)
        && AccessTokenMinutes is >= 1 and <= 10080 && RefreshTokenDays is >= 1 and <= 90
        && MaxFailedAttempts is >= 1 and <= 100 && LockoutMinutes is >= 1 and <= 1440;
}

/// <summary>Кто выполняет запрос (из JWT) и откуда.</summary>
public interface ICurrentUser
{
    Guid? UserId { get; }
    string? Login { get; }
    UserRole? Role { get; }
    string? Ip { get; }
}

public enum PasswordCheck
{
    Failed,
    Success,
    /// <summary>Пароль верный, но хэш устаревшего формата — пересчитать.</summary>
    SuccessRehashNeeded,
}

public interface IPasswordHasher
{
    string Hash(string password);
    PasswordCheck Verify(string hash, string password);
}

public sealed record AccessToken(string Token, DateTimeOffset ExpiresAt);

public interface ITokenIssuer
{
    /// <param name="restaurantId">Ресторан пользователя — claim <c>rid</c>: по нему сервер ограничивает все данные запроса.</param>
    AccessToken Issue(User user, Guid restaurantId, DateTimeOffset now);
}

/// <summary>Ресторан сотрудника — для шапки интерфейса; <see cref="IsDemo"/> — демо-ресторан с симулятором.</summary>
public sealed record RestaurantDto(Guid Id, string Name, bool IsDemo)
{
    public static RestaurantDto From(Mise.Domain.Restaurants.Restaurant r) => new(r.Id, r.Name, r.IsDemo);
}

public sealed record LoginRequest(string Login, string Password);

/// <summary>Демо-вход по роли (только при Demo:Enabled): пароль не хранится во фронтенде.</summary>
public sealed record DemoLoginRequest(UserRole Role);

/// <summary>Что показывать на экране входа.</summary>
public sealed record AuthConfigDto(bool DemoEnabled, IReadOnlyList<UserRole> DemoRoles);

/// <summary>Стартовые демо-учётки (пароль — Auth:DemoUsersPassword).</summary>
public static class DemoUsers
{
    public static readonly IReadOnlyList<(string Login, string DisplayName, UserRole Role)> All =
    [
        ("manager", "Менеджер", UserRole.Manager),
        ("chef", "Шеф-повар", UserRole.Chef),
        ("warehouse", "Кладовщик", UserRole.Warehouse),
    ];

    public static string? LoginFor(UserRole role) => All.FirstOrDefault(u => u.Role == role).Login;
}

public sealed record UserDto(Guid Id, string Login, string DisplayName, UserRole Role, bool IsActive, DateTimeOffset CreatedAt, DateTimeOffset? LastLoginAt)
{
    public static UserDto From(User u) => new(u.Id, u.Login, u.DisplayName, u.Role, u.IsActive, u.CreatedAt, u.LastLoginAt);
}

/// <summary>Результат входа/обновления. Refresh-токен уходит клиенту только в httpOnly-cookie.</summary>
public sealed record AuthSession(
    string AccessToken,
    DateTimeOffset AccessTokenExpiresAt,
    UserDto User,
    RestaurantDto Restaurant,
    string RefreshToken,
    DateTimeOffset RefreshTokenExpiresAt);

/// <summary>Текущий пользователь и его ресторан (GET /auth/me).</summary>
public sealed record MeDto(UserDto User, RestaurantDto Restaurant);

public sealed record CreateUserRequest(string Login, string DisplayName, UserRole Role, string Password);

public sealed record UpdateUserRequest(string DisplayName, UserRole Role, bool IsActive);

public sealed record ResetPasswordRequest(string Password);

internal static class PasswordRules
{
    public const int MinLength = 8;
    public const int MaxLength = 128;

    public static IRuleBuilderOptions<T, string> IsPassword<T>(this IRuleBuilder<T, string> rule) =>
        rule.NotEmpty().MinimumLength(MinLength).MaximumLength(MaxLength);
}

internal sealed class LoginRequestValidator : AbstractValidator<LoginRequest>
{
    public LoginRequestValidator()
    {
        RuleFor(x => x.Login).NotEmpty().MaximumLength(User.LoginMaxLength);
        RuleFor(x => x.Password).NotEmpty().MaximumLength(PasswordRules.MaxLength);
    }
}

internal sealed class CreateUserRequestValidator : AbstractValidator<CreateUserRequest>
{
    public CreateUserRequestValidator()
    {
        RuleFor(x => x.Login).NotEmpty().Must(BeValidLogin).WithMessage("Логин: 3–32 символа, латиница, цифры, «.», «_», «-».");
        RuleFor(x => x.DisplayName).NotEmpty().MaximumLength(100);
        // Служебная роль симулятора создаётся только сидером демо-ресторана, не руками менеджера.
        RuleFor(x => x.Role).IsInEnum().NotEqual(UserRole.Simulator).WithMessage("Роль: менеджер, кладовщик или повар.");
        RuleFor(x => x.Password).IsPassword();
    }

    private static bool BeValidLogin(string login)
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
}

internal sealed class UpdateUserRequestValidator : AbstractValidator<UpdateUserRequest>
{
    public UpdateUserRequestValidator()
    {
        RuleFor(x => x.DisplayName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Role).IsInEnum();
    }
}

internal sealed class ResetPasswordRequestValidator : AbstractValidator<ResetPasswordRequest>
{
    public ResetPasswordRequestValidator() => RuleFor(x => x.Password).IsPassword();
}
