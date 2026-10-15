using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;
using Mise.Simulator.Api;
using Mise.Simulator.Engine;
using StackExchange.Redis;

namespace Mise.Simulator;

// Обычный Main в пространстве имён (не top-level): иначе глобальный тип Program конфликтовал бы с Program из Mise.Api
// в интеграционных тестах, которые ссылаются на обе сборки.
public static class Program
{
    public static async Task Main(string[] args)
    {
        var builder = Host.CreateApplicationBuilder(args);
        builder.Services.AddSimulator(builder.Configuration);
        await builder.Build().RunAsync();
    }

    /// <summary>Облака отдают Redis URL-ом (<c>redis://user:pass@host:6379</c>) — StackExchange.Redis ждёт «host:port,password=…».</summary>
    public static string RedisFromUrl(string value)
    {
        if (!value.Contains("://") || !Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme is not ("redis" or "rediss"))
            return value;
        var parts = new List<string> { $"{uri.Host}:{(uri.Port > 0 ? uri.Port : 6379)}" };
        var info = uri.UserInfo.Split(':', 2);
        if (info.Length == 2 && info[1].Length > 0)
            parts.Add($"password={Uri.UnescapeDataString(info[1])}");
        if (uri.Scheme == "rediss")
            parts.Add("ssl=true");
        return string.Join(',', parts);
    }

    public static IServiceCollection AddSimulator(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<SimulatorOptions>()
            .Bind(configuration.GetSection(SimulatorOptions.SectionName))
            .Validate(o => Uri.TryCreate(o.ApiBaseUrl, UriKind.Absolute, out _) && o.TickMilliseconds is >= 100 and <= 60_000,
                "Simulator: ApiBaseUrl должен быть абсолютным URL, TickMilliseconds — в [100;60000].")
            .ValidateOnStart();

        var redis = configuration.GetConnectionString("Redis")
            ?? throw new InvalidOperationException("ConnectionStrings:Redis is not configured.");
        services.AddSingleton<IConnectionMultiplexer>(_ =>
        {
            var options = ConfigurationOptions.Parse(RedisFromUrl(redis));
            options.AbortOnConnectFail = false;
            return ConnectionMultiplexer.Connect(options);
        });

        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<RetryCounter>();
        services.AddSingleton<ApiTokenCache>();
        services.AddTransient<ApiAuthHandler>();
        services.AddHttpClient(ApiTokenCache.AuthClient, (sp, http) =>
            http.BaseAddress = new Uri(sp.GetRequiredService<IOptions<SimulatorOptions>>().Value.ApiBaseUrl));
        // Ретраи только на сетевые ошибки, 5xx, 408 и 429; POST безопасно повторять — у заказов и поставок Idempotency-Key.
        services.AddHttpClient<IMiseApi, MiseApiClient>((sp, http) =>
                http.BaseAddress = new Uri(sp.GetRequiredService<IOptions<SimulatorOptions>>().Value.ApiBaseUrl))
            .AddHttpMessageHandler<ApiAuthHandler>()
            .AddStandardResilienceHandler()
            .Configure((HttpStandardResilienceOptions options, IServiceProvider sp) =>
            {
                var counter = sp.GetRequiredService<RetryCounter>();
                options.Retry.OnRetry = _ =>
                {
                    counter.Increment();
                    return ValueTask.CompletedTask;
                };
            });

        services.AddSingleton<SimulationEngine>();
        services.AddHostedService<SimulatorWorker>();
        return services;
    }
}
