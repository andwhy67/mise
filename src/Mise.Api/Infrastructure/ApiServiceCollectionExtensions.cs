using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using Mise.Api.Endpoints;
using Mise.Api.Realtime;
using Mise.Application.Realtime;
using Mise.Application.Expiry;
using Mise.Application.HotOffers;
using Mise.Domain.HotOffers;
using Mise.Application.Stock;
using Mise.Infrastructure.Persistence;
using Mise.Infrastructure.Seed;
using Serilog;
using StackExchange.Redis;

namespace Mise.Api.Infrastructure;

internal static class ApiServiceCollectionExtensions
{
    public const string CorsPolicy = "web";

    public static IServiceCollection AddApiServices(this IServiceCollection services, IConfiguration configuration)
    {
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

        services.AddRealtime(configuration);

        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen(options => options.SwaggerDoc("v1", new() { Title = "Mise API", Version = "v1" }));

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
    }

    public static WebApplication UseApiPipeline(this WebApplication app)
    {
        app.UseMiddleware<CorrelationIdMiddleware>();
        app.UseSerilogRequestLogging();
        app.UseExceptionHandler();
        app.UseStatusCodePages();

        if (app.Configuration.GetValue("Swagger:Enabled", app.Environment.IsDevelopment()))
        {
            app.UseSwagger();
            app.UseSwaggerUI(options => options.SwaggerEndpoint("/swagger/v1/swagger.json", "Mise API v1"));
        }

        app.UseCors(CorsPolicy);
        return app;
    }

    public static WebApplication MapApiEndpoints(this WebApplication app)
    {
        app.MapHealthChecks("/health", new HealthCheckOptions
        {
            ResponseWriter = HealthResponseWriter.WriteAsync,
        });

        var v1 = app.MapGroup("/api/v1");
        v1.MapSystemEndpoints();
        v1.MapCatalogEndpoints();
        v1.MapSupplyEndpoints();
        v1.MapStockEndpoints();
        v1.MapOrderEndpoints();
        v1.MapMenuEndpoints();
        v1.MapAlertEndpoints();
        v1.MapHotOfferEndpoints();
        v1.MapSimulatorEndpoints();
        if (app.Configuration.GetValue("Demo:Enabled", false))
            v1.MapDemoEndpoints();

        app.MapHub<StockHub>(StockHub.Path);
        return app;
    }

    /// <summary>Миграции и (по флагу Seed:Enabled) стартовый словарь.</summary>
    public static async Task ApplyMigrationsAsync(this WebApplication app)
    {
        if (!app.Configuration.GetValue("Database:MigrateOnStartup", false))
            return;

        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MiseDbContext>();
        await db.Database.MigrateAsync();
        Log.Information("Database migrations applied");

        if (app.Configuration.GetValue("Seed:Enabled", false))
            await scope.ServiceProvider.GetRequiredService<DatabaseSeeder>().SeedAsync();
    }
}
