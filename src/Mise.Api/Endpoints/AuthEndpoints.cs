using System.Security.Claims;
using Microsoft.Extensions.Options;
using Mise.Api.Auth;
using Mise.Application.Audit;
using Mise.Application.Auth;
using Mise.Application.Common;

namespace Mise.Api.Endpoints;

/// <summary>Ответ входа: access-токен в теле, refresh — только в httpOnly-cookie.</summary>
public sealed record LoginResponse(string AccessToken, DateTimeOffset ExpiresAt, UserDto User);

internal static class AuthEndpoints
{
    public const string RefreshCookie = "mise_refresh";
    public const string RefreshCookiePath = "/api/v1/auth";

    public static RouteGroupBuilder MapAuthEndpoints(this RouteGroupBuilder api)
    {
        var auth = api.MapGroup("/auth").WithTags("Вход");

        auth.MapPost("/login", async (LoginRequest request, AuthService service, HttpContext http, IOptions<AuthOptions> options, CancellationToken ct) =>
            {
                var session = await service.LoginAsync(request, ct);
                SetRefreshCookie(http, session, options.Value);
                return TypedResults.Ok(new LoginResponse(session.AccessToken, session.AccessTokenExpiresAt, session.User));
            })
            .AllowAnonymous()
            .RequireRateLimiting(RateLimitingOptions.LoginPolicy)
            .WithSummary("Вход: access-токен (15 мин) в ответе, refresh — в httpOnly-cookie")
            .WithDescription("Демо-учётки: manager, chef, warehouse (пароль — DEMO_PASSWORD). После 5 неудач — блокировка на 5 мин; 10 попыток в минуту с IP.");

        auth.MapPost("/refresh", async (AuthService service, HttpContext http, IOptions<AuthOptions> options, CancellationToken ct) =>
            {
                try
                {
                    var session = await service.RefreshAsync(http.Request.Cookies[RefreshCookie], ct);
                    SetRefreshCookie(http, session, options.Value);
                    return TypedResults.Ok(new LoginResponse(session.AccessToken, session.AccessTokenExpiresAt, session.User));
                }
                catch (UnauthorizedException ex) when (ex.Code != AuthService.Errors.RefreshRace)
                {
                    ClearRefreshCookie(http, options.Value);
                    throw;
                }
            })
            .AllowAnonymous()
            .RequireRateLimiting(RateLimitingOptions.LoginPolicy)
            .WithSummary("Новая пара токенов по refresh-cookie (ротация; повтор старого токена отзывает всю сессию)");

        auth.MapPost("/logout", async (AuthService service, HttpContext http, IOptions<AuthOptions> options, CancellationToken ct) =>
            {
                await service.LogoutAsync(http.Request.Cookies[RefreshCookie], ct);
                ClearRefreshCookie(http, options.Value);
                return TypedResults.NoContent();
            })
            .AllowAnonymous()
            .WithSummary("Выход: отзыв refresh-токена сессии");

        auth.MapGet("/me", (ClaimsPrincipal user, AuthService service, CancellationToken ct) =>
                service.MeAsync(Guid.Parse(user.FindFirstValue(MiseClaims.UserId)!), ct))
            .WithSummary("Текущий пользователь");

        return api;
    }

    public static RouteGroupBuilder MapUserEndpoints(this RouteGroupBuilder api)
    {
        var users = api.MapGroup("/users").WithTags("Пользователи").RequireAuthorization(AuthPolicies.Manager);

        users.MapGet("/", (UserService service, CancellationToken ct) => service.ListAsync(ct))
            .WithSummary("Пользователи и роли");
        users.MapPost("/", async (CreateUserRequest request, UserService service, CancellationToken ct) =>
                TypedResults.Created($"/api/v1/users", await service.CreateAsync(request, ct)))
            .Audited("user.create", "User")
            .WithSummary("Создать пользователя (роль: Warehouse, Chef, Manager, Simulator)");
        users.MapPut("/{id:guid}", (Guid id, UpdateUserRequest request, UserService service, CancellationToken ct) =>
                service.UpdateAsync(id, request, ct))
            .Audited("user.update", "User")
            .WithSummary("Имя, роль, активность (последнего менеджера отключить нельзя)");
        users.MapPost("/{id:guid}/password", async (Guid id, ResetPasswordRequest request, UserService service, CancellationToken ct) =>
            {
                await service.ResetPasswordAsync(id, request, ct);
                return TypedResults.NoContent();
            })
            .Audited("user.reset_password", "User")
            .WithSummary("Сбросить пароль (сессии пользователя завершаются)");

        return api;
    }

    public static RouteGroupBuilder MapAuditEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/audit", (AuditService service, CancellationToken ct, string? action = null, Guid? userId = null, string? entity = null,
                    string? entityId = null, DateTimeOffset? from = null, DateTimeOffset? to = null, int page = 1, int pageSize = 50) =>
                service.ListAsync(new AuditQuery(action, userId, entity, entityId, from, to), new PageRequest(page, pageSize), ct))
            .RequireAuthorization(AuthPolicies.Manager)
            .WithTags("Аудит")
            .WithSummary("Журнал аудита: кто, что, когда, откуда (новые сверху)");
        return api;
    }

    private static void SetRefreshCookie(HttpContext http, AuthSession session, AuthOptions options) =>
        http.Response.Cookies.Append(RefreshCookie, session.RefreshToken, new CookieOptions
        {
            HttpOnly = true,
            Secure = options.CookieSecure,
            SameSite = SameSiteMode.Strict,
            Path = RefreshCookiePath,
            Expires = session.RefreshTokenExpiresAt,
            IsEssential = true,
        });

    private static void ClearRefreshCookie(HttpContext http, AuthOptions options) =>
        http.Response.Cookies.Delete(RefreshCookie, new CookieOptions
        {
            HttpOnly = true,
            Secure = options.CookieSecure,
            SameSite = SameSiteMode.Strict,
            Path = RefreshCookiePath,
        });
}
