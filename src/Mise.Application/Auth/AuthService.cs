using System.Security.Cryptography;
using System.Text;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Mise.Application.Abstractions;
using Mise.Application.Audit;
using Mise.Application.Common;
using Mise.Domain.Abstractions;
using Mise.Domain.Users;

namespace Mise.Application.Auth;

/// <summary>
/// Вход, ротация refresh-токенов и выход (PLAN §6). Ошибки входа не раскрывают, существует ли логин;
/// после серии неудач аккаунт блокируется; повторное использование отозванного refresh отзывает всю цепочку.
/// </summary>
public sealed class AuthService(
    IMiseDbContext db,
    IClock clock,
    IStockLocks locks,
    IPasswordHasher hasher,
    ITokenIssuer issuer,
    AuditService audit,
    IOptions<AuthOptions> options,
    IValidator<LoginRequest> loginValidator)
{
    public static class Errors
    {
        public const string InvalidCredentials = "auth.invalid_credentials";
        public const string Locked = "auth.locked";
        public const string Inactive = "auth.inactive";
        public const string RefreshInvalid = "auth.refresh_invalid";
        public const string RefreshRace = "auth.refresh_race";
        public const string DemoUnavailable = "auth.demo_unavailable";
    }

    public static class Actions
    {
        public const string Login = "auth.login";
        public const string DemoLogin = "auth.demo_login";
        public const string LoginFailed = "auth.login_failed";
        public const string Logout = "auth.logout";
        public const string RefreshReuse = "auth.refresh_reuse";
    }

    /// <summary>Отозванный при ротации токен, пришедший повторно в течение этого окна, — гонка вкладок, а не кража.</summary>
    public static readonly TimeSpan RotationGrace = TimeSpan.FromSeconds(10);

    // Хэш для несуществующего логина: проверка занимает столько же времени, сколько для реального пользователя.
    private static readonly Lazy<string> DummyHash = new(() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(48)));

    public async Task<AuthSession> LoginAsync(LoginRequest request, CancellationToken ct)
    {
        await loginValidator.EnsureValidAsync(request, ct);
        var settings = options.Value;
        var now = clock.UtcNow;
        var login = TryNormalize(request.Login);
        var user = login is null ? null : await db.Users.FirstOrDefaultAsync(u => u.Login == login, ct);

        if (user is null)
        {
            hasher.Verify(DummyHash.Value, request.Password);
            await audit.RecordAsync(Actions.LoginFailed, null, login ?? request.Login.Trim(), "User", null, new { reason = "unknown_login" }, ct);
            throw InvalidCredentials();
        }

        if (user.IsLockedOut(now))
        {
            await audit.RecordAsync(Actions.LoginFailed, user.Id, user.Login, "User", user.Id.ToString(), new { reason = "locked" }, ct);
            throw Locked(user, now);
        }

        var check = hasher.Verify(user.PasswordHash, request.Password);
        if (check == PasswordCheck.Failed)
        {
            user.RegisterFailedLogin(now, settings.Lockout);
            await db.SaveChangesAsync(ct);
            await audit.RecordAsync(Actions.LoginFailed, user.Id, user.Login, "User", user.Id.ToString(),
                new { reason = "wrong_password", failedAttempts = user.FailedLoginCount }, ct);
            throw user.IsLockedOut(now) ? Locked(user, now) : InvalidCredentials();
        }

        if (!user.IsActive)
        {
            await audit.RecordAsync(Actions.LoginFailed, user.Id, user.Login, "User", user.Id.ToString(), new { reason = "inactive" }, ct);
            throw new UnauthorizedException(Errors.Inactive, "Учётная запись отключена. Обратитесь к менеджеру.");
        }

        if (check == PasswordCheck.SuccessRehashNeeded)
            user.SetPasswordHash(hasher.Hash(request.Password));
        user.RegisterSuccessfulLogin(now);
        var session = IssueSession(user, familyId: Guid.CreateVersion7(now), now, out _);
        await db.SaveChangesAsync(ct);
        await audit.RecordAsync(Actions.Login, user.Id, user.Login, "User", user.Id.ToString(), new { role = user.Role.ToString() }, ct);
        return session;
    }

    /// <summary>
    /// Демо-вход по роли (вызывается, только если Demo:Enabled): та же сессия, что и при обычном входе, с аудитом.
    /// </summary>
    public async Task<AuthSession> DemoLoginAsync(DemoLoginRequest request, CancellationToken ct)
    {
        var login = DemoUsers.LoginFor(request.Role)
            ?? throw new[] { new FluentValidation.Results.ValidationFailure("role", "Демо-вход доступен для ролей Warehouse, Chef и Manager.") }
                .ToValidationException();
        var user = await db.Users.FirstOrDefaultAsync(u => u.Login == login && u.IsActive, ct)
            ?? throw new UnauthorizedException(Errors.DemoUnavailable, "Демо-учётка не настроена (Auth:DemoUsersPassword).");

        var now = clock.UtcNow;
        user.RegisterSuccessfulLogin(now);
        var session = IssueSession(user, Guid.CreateVersion7(now), now, out _);
        await db.SaveChangesAsync(ct);
        await audit.RecordAsync(Actions.DemoLogin, user.Id, user.Login, "User", user.Id.ToString(), new { role = user.Role.ToString() }, ct);
        return session;
    }

    public async Task<AuthSession> RefreshAsync(string? rawToken, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(rawToken))
            throw RefreshInvalid();
        var hash = HashToken(rawToken);

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        // Параллельные обновления одним токеном (две вкладки) выполняются по очереди.
        await locks.AcquireTransactionLockAsync("refresh:" + hash, ct);
        var now = clock.UtcNow;
        var token = await db.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == hash, ct) ?? throw RefreshInvalid();

        if (token.RevokedAt is { } revokedAt)
        {
            if (token.RevokeReason == RefreshTokenRevocation.Rotated && now - revokedAt < RotationGrace)
                throw new UnauthorizedException(Errors.RefreshRace, "Сессия уже обновлена в другой вкладке — повторите запрос.");

            var revoked = await RevokeFamilyAsync(token.FamilyId, RefreshTokenRevocation.Reused, now, ct);
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            await audit.RecordAsync(Actions.RefreshReuse, token.UserId, null, "User", token.UserId.ToString(),
                new { familyId = token.FamilyId, revoked }, ct);
            throw RefreshInvalid();
        }
        if (!token.IsActiveAt(now))
            throw RefreshInvalid();

        var user = await db.Users.FirstAsync(u => u.Id == token.UserId, ct);
        if (!user.IsActive)
        {
            await RevokeFamilyAsync(token.FamilyId, RefreshTokenRevocation.UserChanged, now, ct);
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            throw new UnauthorizedException(Errors.Inactive, "Учётная запись отключена.");
        }

        var session = IssueSession(user, token.FamilyId, now, out var next);
        token.Revoke(now, RefreshTokenRevocation.Rotated, next.Id);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return session;
    }

    public async Task LogoutAsync(string? rawToken, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(rawToken))
            return;
        var hash = HashToken(rawToken);
        var token = await db.RefreshTokens.AsNoTracking().FirstOrDefaultAsync(t => t.TokenHash == hash, ct);
        if (token is null)
            return;

        await RevokeFamilyAsync(token.FamilyId, RefreshTokenRevocation.Logout, clock.UtcNow, ct);
        await db.SaveChangesAsync(ct);
        await audit.RecordAsync(Actions.Logout, token.UserId, null, "User", token.UserId.ToString(), null, ct);
    }

    public async Task<UserDto> MeAsync(Guid userId, CancellationToken ct)
    {
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, ct);
        return user is { IsActive: true }
            ? UserDto.From(user)
            : throw new UnauthorizedException(Errors.Inactive, "Учётная запись не найдена или отключена.");
    }

    /// <summary>Отзывает все активные токены пользователя (смена пароля, роли, отключение).</summary>
    internal static async Task RevokeAllAsync(IMiseDbContext db, Guid userId, DateTimeOffset now, CancellationToken ct)
    {
        var active = await db.RefreshTokens.Where(t => t.UserId == userId && t.RevokedAt == null).ToListAsync(ct);
        active.ForEach(t => t.Revoke(now, RefreshTokenRevocation.UserChanged));
    }

    public static string HashToken(string rawToken) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(rawToken)));

    private AuthSession IssueSession(User user, Guid familyId, DateTimeOffset now, out RefreshToken refresh)
    {
        var access = issuer.Issue(user, now);
        var raw = Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        refresh = new RefreshToken(Guid.CreateVersion7(now), user.Id, HashToken(raw), familyId, now,
            now.AddDays(options.Value.RefreshTokenDays));
        db.RefreshTokens.Add(refresh);
        return new AuthSession(access.Token, access.ExpiresAt, UserDto.From(user), raw, refresh.ExpiresAt);
    }

    private async Task<int> RevokeFamilyAsync(Guid familyId, RefreshTokenRevocation reason, DateTimeOffset now, CancellationToken ct)
    {
        var active = await db.RefreshTokens.Where(t => t.FamilyId == familyId && t.RevokedAt == null).ToListAsync(ct);
        active.ForEach(t => t.Revoke(now, reason));
        return active.Count;
    }

    private static string? TryNormalize(string login)
    {
        try
        {
            return User.NormalizeLogin(login);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private static UnauthorizedException InvalidCredentials() =>
        new(Errors.InvalidCredentials, "Неверный логин или пароль.");

    private static UnauthorizedException Locked(User user, DateTimeOffset now)
    {
        var minutes = Math.Max(1, (int)Math.Ceiling((user.LockoutUntil!.Value - now).TotalMinutes));
        return new UnauthorizedException(Errors.Locked, $"Слишком много неудачных попыток. Повторите через {minutes} мин.");
    }

    private static UnauthorizedException RefreshInvalid() =>
        new(Errors.RefreshInvalid, "Сессия истекла или недействительна — войдите заново.");

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
