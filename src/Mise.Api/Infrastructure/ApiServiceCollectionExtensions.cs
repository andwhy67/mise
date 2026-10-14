using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.OpenApi.Models;
using Mise.Api.Auth;
using Mise.Api.Endpoints;
using Mise.Application.Push;
using Mise.Infrastructure.Push;
using Mise.Api.Realtime;
using Mise.Application.Expiry;
using Mise.Application.Forecasting;
using Mise.Application.HotOffers;
using Mise.Application.Realtime;
using Mise.Application.Stock;
using Mise.Domain.HotOffers;
using Mise.Infrastructure.Persistence;
using Serilog;
using StackExchange.Redis;

namespace Mise.Api.Infrastructure;

internal static class ApiServiceCollectionExtensions
{
    public const string CorsPolicy = "web";

    public static IServiceCollection AddApiServices(this IServiceCollection services, IConfiguration configuration)
    {
        // Первым: остальные фоновые задачи и HTTP-сервер стартуют только после подготовки БД.
        services.AddHostedService<DatabaseInitializer>();

        services.AddProblemDetails(options => options.CustomizeProblemDetails = ctx =>
        {
            ctx.ProblemDetails.Instance ??= ctx.HttpContext.Request.Path;
            ctx.ProblemDetails.Extensions["traceId"] = ctx.HttpContext.TraceIdentifier;
        });
        services.AddExceptionHandler<GlobalExceptionHandler>();
        // Ошибки привязки (битый JSON, неизвестное значение enum) — тоже через GlobalExceptionHandler, с пояснением.
        services.Configure<RouteHandlerOptions>(options => options.ThrowOnBadRequest = true);

        // Перечисления в JSON — строками ("Gram", "Warning"), в том числе во входящих запросах.
        services.ConfigureHttpJsonOptions(options => options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

        services.AddOptions<StockOptions>()
            .Bind(configuration.GetSection(StockOptions.SectionName))
            .Validate(o =>
            {
                try { o.ShelfLife.ToThresholds(); return true; }
                catch (ArgumentException) { return false; }
            }, "Stock:ShelfLife: некорректные пороги статусов срока.")
            .ValidateOnStart();

        services.AddOptions<ExpiryOptions>()
            .Bind(configuration.GetSection(ExpiryOptions.SectionName))
            .Validate(o => o.IsValid(), "Expiry: IntervalSeconds должен быть в [1;3600], BatchSize — в [1;10000].")
            .ValidateOnStart();
        services.AddHostedService<ExpiryWorker>();

        services.AddOptions<HotOfferOptions>()
            .Bind(configuration.GetSection(HotOfferOptions.SectionName))
            .Validate<IEnumerable<IHotOfferScorer>>((o, scorers) => o.IsValid(scorers.Select(s => s.Version)),
                "HotOffers: неизвестный Scorer или некорректные параметры (см. docs/hot-offers.md).")
            .ValidateOnStart();
        services.AddHostedService<HotOffersTicker>();

        services.AddOptions<ForecastOptions>()
            .Bind(configuration.GetSection(ForecastOptions.SectionName))
            .Validate(o => o.IsValid(), "Forecast: некорректные параметры прогноза (см. docs/assumptions.md, G*).")
            .ValidateOnStart();
        services.AddHostedService<ConsumptionAggregationWorker>();

        services.AddRealtime(configuration);

        services.AddMiseSecurity(configuration);

        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen(options =>
        {
            options.SwaggerDoc("v1", new() { Title = "Mise API", Version = "v1" });
            var bearer = new OpenApiSecurityScheme
            {
                Name = "Authorization",
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                BearerFormat = "JWT",
                In = ParameterLocation.Header,
                Description = "Access-токен из POST /api/v1/auth/login",
                Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" },
            };
            options.AddSecurityDefinition("Bearer", bearer);
            options.AddSecurityRequirement(new OpenApiSecurityRequirement { [bearer] = [] });
        });

        var origins = (configuration["Cors:AllowedOrigins"] ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        services.AddCors(options => options.AddPolicy(CorsPolicy, policy => policy
            .WithOrigins(origins)
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials()));

        services.AddHealthChecks()
            .AddDbContextCheck<MiseDbContext>("postgres", tags: ["ready"])
            .AddRedis(sp => sp.GetRequiredService<IConnectionMultiplexer>(), "redis", tags: ["ready"]);

        return services;
    }

    /// <summary>SignalR (+ Redis backplane), буфер изменений и фоновая рассылка, ретрансляция статуса симулятора.</summary>
    private static void AddRealtime(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<RealtimeOptions>()
            .Bind(configuration.GetSection(RealtimeOptions.SectionName))
            .Validate(o => o.IsValid(), "Realtime:DebounceMilliseconds должен быть в [0;10000].")
            .ValidateOnStart();

        var redis = configuration.GetConnectionString("Redis")
            ?? throw new InvalidOperationException("ConnectionStrings:Redis is not configured.");
        services.AddSignalR()
            .AddJsonProtocol(options => options.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter()))
            .AddStackExchangeRedis(redis, options =>
            {
                options.Configuration.ChannelPrefix = RedisChannel.Literal("mise:signalr");
                options.Configuration.AbortOnConnectFail = false;
            });

        services.AddSingleton<RealtimeChangeBuffer>();
        services.AddSingleton<IRealtimeNotifier>(sp => sp.GetRequiredService<RealtimeChangeBuffer>());
        services.AddHostedService<RealtimePublisher>();
        services.AddHostedService<SimulatorStatusRelay>();

        // Пуши на телефон: новые алерты из RealtimePublisher → очередь → рассылка (Web Push, VAPID из env).
        services.AddOptions<PushOptions>()
            .Bind(configuration.GetSection(PushOptions.SectionName))
            .Validate(o => o.IsValid(), "Push: Subject — mailto: или https:, AllowedHosts не пуст, MaxPerDevice в [1;20].")
            .ValidateOnStart();
        services.AddHttpClient<IPushSender, WebPushSender>(client => client.Timeout = TimeSpan.FromSeconds(10));
        services.AddSingleton<PushQueue>();
        services.AddHostedService<PushWorker>();
    }

    public static WebApplication UseApiPipeline(this WebApplication app)
    {
        app.UseForwardedHeaders();
        app.UseMiddleware<CorrelationIdMiddleware>();
        app.UseMiddleware<SecurityHeadersMiddleware>();
        app.UseSerilogRequestLogging();
        app.UseExceptionHandler();
        app.UseStatusCodePages();

        if (app.Configuration.GetValue("Swagger:Enabled", app.Environment.IsDevelopment()))
        {
            app.UseSwagger();
            app.UseSwaggerUI(options => options.SwaggerEndpoint("/swagger/v1/swagger.json", "Mise API v1"));
        }

        if (!app.Environment.IsDevelopment() && app.Configuration.GetValue("Security:Hsts", true))
            app.UseHsts();

        app.UseCors(CorsPolicy);
        app.UseAuthentication();
        app.UseRateLimiter();
        app.UseAuthorization();
        return app;
    }

    public static WebApplication MapApiEndpoints(this WebApplication app)
    {
        app.MapHealthChecks("/health", new HealthCheckOptions
        {
            ResponseWriter = HealthResponseWriter.WriteAsync,
        }).AllowAnonymous();

        var v1 = app.MapGroup("/api/v1");
        v1.MapSystemEndpoints();
        v1.MapAuthEndpoints();
        v1.MapUserEndpoints();
        v1.MapAuditEndpoints();
        v1.MapCatalogEndpoints();
        v1.MapSupplyEndpoints();
        v1.MapStockEndpoints();
        v1.MapOrderEndpoints();
        v1.MapMenuEndpoints();
        v1.MapAlertEndpoints();
        v1.MapPushEndpoints();
        v1.MapHotOfferEndpoints();
        v1.MapForecastEndpoints();
        v1.MapAnalyticsEndpoints();
        v1.MapSimulatorEndpoints();
        if (app.Configuration.GetValue("Demo:Enabled", false))
            v1.MapDemoEndpoints();

        app.MapHub<StockHub>(StockHub.Path);
        return app;
    }
}
