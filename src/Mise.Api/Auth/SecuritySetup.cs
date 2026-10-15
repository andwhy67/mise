using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Mise.Api.Realtime;
using Mise.Application.Auth;
using Mise.Domain.Abstractions;
using Mise.Domain.Users;

namespace Mise.Api.Auth;

/// <summary>Политики доступа по ролям (PLAN §5–6). По умолчанию любой эндпоинт требует входа (fallback policy).</summary>
public static class AuthPolicies
{
    public const string Manager = "manager";
    /// <summary>Приём поставок: склад, менеджер, симулятор.</summary>
    public const string Supplies = "supplies";
    /// <summary>Заказы: кухня, менеджер, симулятор.</summary>
    public const string Orders = "orders";
    /// <summary>Склад: ручное списание, предложения поставок.</summary>
    public const string Warehouse = "warehouse";
    /// <summary>Персонал (не служебные учётки): подтверждение алертов.</summary>
    public const string Staff = "staff";
    /// <summary>Дашборд: KPI.</summary>
    public const string Dashboard = "dashboard";

    internal static void Add(Microsoft.AspNetCore.Authorization.AuthorizationOptions options)
    {
        options.FallbackPolicy = new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build();
        options.AddPolicy(Manager, p => p.RequireRole(nameof(UserRole.Manager)));
        options.AddPolicy(Supplies, p => p.RequireRole(nameof(UserRole.Warehouse), nameof(UserRole.Manager), nameof(UserRole.Simulator)));
        options.AddPolicy(Orders, p => p.RequireRole(nameof(UserRole.Chef), nameof(UserRole.Manager), nameof(UserRole.Simulator)));
        options.AddPolicy(Warehouse, p => p.RequireRole(nameof(UserRole.Warehouse), nameof(UserRole.Manager)));
        options.AddPolicy(Staff, p => p.RequireRole(nameof(UserRole.Warehouse), nameof(UserRole.Chef), nameof(UserRole.Manager)));
        options.AddPolicy(Dashboard, p => p.RequireRole(nameof(UserRole.Chef), nameof(UserRole.Manager)));
    }
}

/// <summary>Лимиты запросов (секция "RateLimiting").</summary>
public sealed class RateLimitingOptions
{
    public const string SectionName = "RateLimiting";
    public const string LoginPolicy = "login";
    public const string GuestOrderPolicy = "guest-order";

    /// <summary>Попыток входа в минуту с одного IP.</summary>
    public int LoginPerMinute { get; set; } = 10;

    /// <summary>Запросов в минуту на пользователя (или IP без входа); служебная роль Simulator не ограничивается.</summary>
    public int GlobalPerMinute { get; set; } = 600;

    /// <summary>Заказов гостя (QR-меню) с одного IP за <see cref="GuestOrderWindowMinutes"/> минут — защита кухни от спама.</summary>
    public int GuestOrdersPerWindow { get; set; } = 3;

    public int GuestOrderWindowMinutes { get; set; } = 10;

    public bool IsValid() => LoginPerMinute >= 1 && GlobalPerMinute >= 1 && GuestOrdersPerWindow >= 1 && GuestOrderWindowMinutes >= 1;
}

internal sealed class HttpCurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    private ClaimsPrincipal? Principal => accessor.HttpContext?.User;

    public Guid? UserId => Guid.TryParse(Principal?.FindFirstValue(MiseClaims.UserId), out var id) ? id : null;
    public string? Login => Principal?.FindFirstValue(MiseClaims.Login);
    public UserRole? Role => Enum.TryParse<UserRole>(Principal?.FindFirstValue(MiseClaims.Role), out var role) ? role : null;
    public string? Ip => accessor.HttpContext?.Connection.RemoteIpAddress?.ToString();
}

internal static class SecuritySetup
{
    public static IServiceCollection AddMiseSecurity(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<AuthOptions>()
            .Bind(configuration.GetSection(AuthOptions.SectionName))
            .Validate(o => o.IsValid(), $"Auth: нужен SigningKey не короче {AuthOptions.MinSigningKeyBytes} байт (env Auth__SigningKey) и корректные сроки.")
            .ValidateOnStart();
        services.AddOptions<RateLimitingOptions>()
            .Bind(configuration.GetSection(RateLimitingOptions.SectionName))
            .Validate(o => o.IsValid(), "RateLimiting: лимиты должны быть положительными.")
            .ValidateOnStart();

        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUser, HttpCurrentUser>();
        services.AddSingleton<ITokenIssuer, JwtTokenIssuer>();

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IOptions<AuthOptions>, IClock>((jwt, auth, clock) =>
            {
                var settings = auth.Value;
                jwt.MapInboundClaims = false;
                jwt.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidIssuer = settings.Issuer,
                    ValidAudience = settings.Audience,
                    IssuerSigningKey = JwtTokenIssuer.SigningKey(settings),
                    ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
                    NameClaimType = MiseClaims.Login,
                    RoleClaimType = MiseClaims.Role,
                    ClockSkew = TimeSpan.FromSeconds(30),
                    // Срок — по IClock, а не по системным часам: время в системе одно (CLAUDE.md, правило 7).
                    LifetimeValidator = (notBefore, expires, _, parameters) =>
                    {
                        var now = clock.UtcNow.UtcDateTime;
                        return (notBefore is null || now + parameters.ClockSkew >= notBefore.Value)
                               && expires is { } until && now - parameters.ClockSkew < until;
                    },
                };
                jwt.Events = new JwtBearerEvents
                {
                    // WebSocket/SSE не умеют заголовки: SignalR передаёт токен в access_token (только для хаба).
                    OnMessageReceived = context =>
                    {
                        var token = context.Request.Query["access_token"];
                        if (!string.IsNullOrEmpty(token) && context.HttpContext.Request.Path.StartsWithSegments(StockHub.Path))
                            context.Token = token;
                        return Task.CompletedTask;
                    },
                };
            });
        services.AddAuthorization(AuthPolicies.Add);

        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = async (context, ct) =>
            {
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                    context.HttpContext.Response.Headers.RetryAfter = ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString();
                await TypedResults.Problem(statusCode: StatusCodes.Status429TooManyRequests, title: "Слишком много запросов",
                        detail: "Превышен лимит запросов — повторите позже.", extensions: new Dictionary<string, object?> { ["code"] = "rate_limited" })
                    .ExecuteAsync(context.HttpContext);
            };
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
            {
                var limits = context.RequestServices.GetRequiredService<IOptions<RateLimitingOptions>>().Value;
                if (context.User.IsInRole(nameof(UserRole.Simulator)))
                    return RateLimitPartition.GetNoLimiter("simulator");
                var key = context.User.FindFirstValue(MiseClaims.UserId) ?? "ip:" + context.Connection.RemoteIpAddress;
                return RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = limits.GlobalPerMinute,
                    Window = TimeSpan.FromMinutes(1),
                });
            });
            options.AddPolicy(RateLimitingOptions.LoginPolicy, context =>
            {
                var limits = context.RequestServices.GetRequiredService<IOptions<RateLimitingOptions>>().Value;
                return RateLimitPartition.GetFixedWindowLimiter("login:" + context.Connection.RemoteIpAddress, _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = limits.LoginPerMinute,
                    Window = TimeSpan.FromMinutes(1),
                });
            });
            options.AddPolicy(RateLimitingOptions.GuestOrderPolicy, context =>
            {
                var limits = context.RequestServices.GetRequiredService<IOptions<RateLimitingOptions>>().Value;
                return RateLimitPartition.GetFixedWindowLimiter("guest:" + context.Connection.RemoteIpAddress, _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = limits.GuestOrdersPerWindow,
                    Window = TimeSpan.FromMinutes(limits.GuestOrderWindowMinutes),
                });
            });
        });

        // За nginx / прокси платформы: реальный IP клиента и схема https. Берём только последние ForwardLimit звеньев
        // X-Forwarded-For (их дописали наши прокси) — значения, присланные клиентом, левее и не учитываются.
        services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            if (configuration.GetValue("ForwardedHeaders:TrustAllProxies", false))
            {
                options.KnownNetworks.Clear();
                options.KnownProxies.Clear();
                options.ForwardLimit = Math.Max(1, configuration.GetValue("ForwardedHeaders:ForwardLimit", 1));
            }
        });

        return services;
    }
}
