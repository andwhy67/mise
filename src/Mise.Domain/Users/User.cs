using System.Text.RegularExpressions;

namespace Mise.Domain.Users;

/// <summary>Роли (PLAN §6): склад, кухня, менеджер и служебная роль симулятора.</summary>
public enum UserRole
{
    Warehouse,
    Chef,
    Manager,
    Simulator,
}

/// <summary>Блокировка после серии неудачных входов.</summary>
public sealed record LockoutPolicy(int MaxFailedAttempts, TimeSpan Duration);

/// <summary>Пользователь системы. Пароль хранится только хэшем (формат — ASP.NET Identity PasswordHasher).</summary>
public sealed partial class User
{
    public const int LoginMinLength = 3;
    public const int LoginMaxLength = 32;

    private User() { }

    public Guid Id { get; private set; }
    /// <summary>Логин в нижнем регистре: [a-z0-9._-], 3–32 символа.</summary>
    public string Login { get; private set; } = string.Empty;
    public string DisplayName { get; private set; } = string.Empty;
    public UserRole Role { get; private set; }
    public string PasswordHash { get; private set; } = string.Empty;
    public bool IsActive { get; private set; }
    public int FailedLoginCount { get; private set; }
    public DateTimeOffset? LockoutUntil { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? LastLoginAt { get; private set; }

    public static User Create(Guid id, string login, string displayName, UserRole role, string passwordHash, DateTimeOffset now) => new()
    {
        Id = id,
        Login = NormalizeLogin(login),
        DisplayName = RequireName(displayName),
        Role = role,
        PasswordHash = passwordHash,
        IsActive = true,
        CreatedAt = now,
    };

    /// <summary>Логин в каноническом виде; бросает <see cref="ArgumentException"/>, если формат неверный.</summary>
    public static string NormalizeLogin(string login)
    {
        var normalized = (login ?? string.Empty).Trim().ToLowerInvariant();
        if (!LoginPattern().IsMatch(normalized))
            throw new ArgumentException($"Логин: {LoginMinLength}–{LoginMaxLength} символов, латиница, цифры, «.», «_», «-».", nameof(login));
        return normalized;
    }

    public bool IsLockedOut(DateTimeOffset now) => LockoutUntil is { } until && now < until;

    public void RegisterFailedLogin(DateTimeOffset now, LockoutPolicy policy)
    {
        // Истёкшая блокировка — новая серия попыток.
        if (LockoutUntil is { } until && now >= until)
        {
            FailedLoginCount = 0;
            LockoutUntil = null;
        }
        FailedLoginCount++;
        if (FailedLoginCount >= policy.MaxFailedAttempts)
            LockoutUntil = now + policy.Duration;
    }

    public void RegisterSuccessfulLogin(DateTimeOffset now)
    {
        FailedLoginCount = 0;
        LockoutUntil = null;
        LastLoginAt = now;
    }

    public void Update(string displayName, UserRole role, bool isActive)
    {
        DisplayName = RequireName(displayName);
        Role = role;
        IsActive = isActive;
    }

    public void SetPasswordHash(string passwordHash)
    {
        PasswordHash = passwordHash;
        FailedLoginCount = 0;
        LockoutUntil = null;
    }

    private static string RequireName(string name) =>
        string.IsNullOrWhiteSpace(name) ? throw new ArgumentException("Имя не может быть пустым.", nameof(name)) : name.Trim();

    [GeneratedRegex("^[a-z0-9._-]{3,32}$")]
    private static partial Regex LoginPattern();
}

public enum RefreshTokenRevocation
{
    /// <summary>Заменён новым при обновлении (ротация).</summary>
    Rotated,
    /// <summary>Выход пользователя.</summary>
    Logout,
    /// <summary>Повторное использование уже заменённого токена — вся цепочка отозвана (признак кражи).</summary>
    Reused,
    /// <summary>Пользователь отключён или сменил пароль.</summary>
    UserChanged,
}

/// <summary>
/// Refresh-токен: в БД — только SHA-256 хэш. Токены одной сессии образуют цепочку (<see cref="FamilyId"/>):
/// при ротации старый отзывается, при повторном использовании отозванного — отзывается вся цепочка.
/// </summary>
public sealed class RefreshToken
{
    private RefreshToken() { }

    public RefreshToken(Guid id, Guid userId, string tokenHash, Guid familyId, DateTimeOffset createdAt, DateTimeOffset expiresAt)
    {
        Id = id;
        UserId = userId;
        TokenHash = tokenHash;
        FamilyId = familyId;
        CreatedAt = createdAt;
        ExpiresAt = expiresAt;
    }

    public Guid Id { get; private set; }
    public Guid UserId { get; private set; }
    public string TokenHash { get; private set; } = string.Empty;
    public Guid FamilyId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }
    public DateTimeOffset? RevokedAt { get; private set; }
    public RefreshTokenRevocation? RevokeReason { get; private set; }
    public Guid? ReplacedById { get; private set; }

    public bool IsActiveAt(DateTimeOffset now) => RevokedAt is null && now < ExpiresAt;

    /// <summary>Идемпотентно: первая причина отзыва сохраняется.</summary>
    public void Revoke(DateTimeOffset now, RefreshTokenRevocation reason, Guid? replacedBy = null)
    {
        if (RevokedAt is not null)
            return;
        RevokedAt = now;
        RevokeReason = reason;
        ReplacedById = replacedBy;
    }
}
