using System.Collections.Concurrent;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Mise.Application.Abstractions;
using Mise.Application.Auth;
using Mise.Application.Expiry;
using Mise.Application.Push;
using Mise.Domain.Abstractions;
using Mise.Domain.Users;
using Mise.Infrastructure.Persistence;
using Mise.Infrastructure.Push;
using Mise.Infrastructure.Seed;
using Mise.Infrastructure.Time;
using Testcontainers.PostgreSql;
using Testcontainers.Redis;

namespace Mise.Api.IntegrationTests;

/// <summary>
/// Поднимает API поверх настоящих Postgres и Redis в контейнерах (один набор на коллекцию тестов).
/// Время управляемое (<see cref="Clock"/>), словарь засеян стартовыми продуктами и блюдами, пользователи — демо-учётками.
/// <see cref="WebApplicationFactory{TEntryPoint}.CreateClient()"/> по умолчанию входит как Manager и представляется отдельным IP
/// (лимиты запросов считаются по клиенту); другие роли — <see cref="CreateClientAs"/>.
/// </summary>
public sealed class MiseApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public static readonly DateTimeOffset StartTime = new(2026, 10, 12, 8, 0, 0, TimeSpan.Zero);
    public const string DemoPassword = "Test-Pass-123";
    public const string SimulatorPassword = "Test-Sim-123";
    public const string ReadOnlyUser = "mise_readonly";
    public const string ReadOnlyPassword = "ro_test_pass";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine")
        .WithDatabase("mise")
        .WithUsername("mise")
        .WithPassword("mise_test")
        .Build();

    private readonly RedisContainer _redis = new RedisBuilder("redis:7-alpine")
        .Build();

    private readonly ConcurrentDictionary<UserRole, string> _tokens = new();
    private int _clientCounter;
    private UserRole? _nextClientRole = UserRole.Manager;

    public FakeClock Clock { get; } = new(StartTime);

    /// <summary>Пуши в тестах не уходят в сеть: подставной отправитель их записывает.</summary>
    public PushRecorder Push { get; } = new(WebPushCrypto.GenerateVapidKeys());

    public string PostgresConnectionString => _postgres.GetConnectionString();

    public async Task InitializeAsync()
    {
        await Task.WhenAll(_postgres.StartAsync(), _redis.StartAsync());
    }

    /// <summary>Клиент с ролью (null — без входа).</summary>
    public HttpClient CreateClientAs(UserRole? role)
    {
        lock (_tokens)
        {
            _nextClientRole = role;
            try
            {
                return CreateClient();
            }
            finally
            {
                _nextClientRole = UserRole.Manager;
            }
        }
    }

    /// <summary>Access-токен демо-пользователя роли (выпускается тем же сервисом, что и при входе).</summary>
    public string TokenFor(UserRole role) => _tokens.GetOrAdd(role, r =>
    {
        using var scope = Services.CreateScope();
        var login = r == UserRole.Simulator ? UserSeeder.SimulatorLogin : DemoUsers.LoginFor(r)!;
        var user = scope.ServiceProvider.GetRequiredService<MiseDbContext>().Users.IgnoreQueryFilters().AsNoTracking().Single(u => u.Login == login);
        return scope.ServiceProvider.GetRequiredService<ITokenIssuer>().Issue(user, SeedData.DemoRestaurantId, Clock.UtcNow).Token;
    });

    protected override void ConfigureClient(HttpClient client)
    {
        base.ConfigureClient(client);
        var n = Interlocked.Increment(ref _clientCounter);
        client.DefaultRequestHeaders.Add("X-Forwarded-For", $"10.{n / 65536 % 256}.{n / 256 % 256}.{n % 256}");
        if (_nextClientRole is { } role)
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenFor(role));
    }

    /// <summary>
    /// Тесты работают в демо-ресторане (демо-учётки): прямой доступ к БД — от его имени, иначе фильтр ресторана
    /// честно вернёт пустоту. Синхронный вызов: значение AsyncLocal остаётся у вызывающего до Dispose.
    /// </summary>
    public IDisposable AsDemoRestaurant() => Services.GetRequiredService<ITenant>().Use(SeedData.DemoRestaurantId);

    /// <summary>Один тик мониторинга сроков на текущем времени <see cref="Clock"/> (фоновый воркер в тестах выключен).</summary>
    public async Task<ExpiryRunResult> RunExpiryTickAsync(bool autoWriteOff = true)
    {
        await using var scope = Services.CreateAsyncScope();
        // Тесты работают в демо-ресторане (демо-учётки) — тик сроков тоже его.
        using var tenant = scope.ServiceProvider.GetRequiredService<ITenant>().Use(SeedData.DemoRestaurantId);
        var service = ActivatorUtilities.CreateInstance<ExpiryMonitorService>(scope.ServiceProvider,
            Options.Create(new ExpiryOptions { AutoWriteOff = autoWriteOff }));
        return await service.RunOnceAsync(CancellationToken.None);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Postgres", _postgres.GetConnectionString());
        builder.UseSetting("ConnectionStrings:Redis", _redis.GetConnectionString());
        builder.UseSetting("Database:MigrateOnStartup", "true");
        builder.UseSetting("Database:ReadOnlyUser", ReadOnlyUser);
        builder.UseSetting("Database:ReadOnlyPassword", ReadOnlyPassword);
        builder.UseSetting("Seed:Enabled", "true");
        builder.UseSetting("Cors:AllowedOrigins", "http://localhost:5173");
        // Тик мониторинга сроков тесты вызывают сами (RunExpiryTickAsync) — детерминированно, вместе с FakeClock.
        builder.UseSetting("Expiry:Enabled", "false");
        builder.UseSetting("Realtime:DebounceMilliseconds", "50");
        builder.UseSetting("Demo:Enabled", "true");
        builder.UseSetting("Auth:SigningKey", "integration-tests-signing-key-0123456789abcdef");
        builder.UseSetting("Auth:DemoUsersPassword", DemoPassword);
        builder.UseSetting("Auth:SimulatorPassword", SimulatorPassword);
        builder.UseSetting("Auth:CookieSecure", "false");
        // Тесты двигают FakeClock на десятки часов внутри одного теста — токен клиента должен это пережить.
        builder.UseSetting("Auth:AccessTokenMinutes", "10080");
        builder.UseSetting("ForwardedHeaders:TrustAllProxies", "true");
        // Все клиенты тестов — один Manager: общий лимит на пользователя поднят; лимит входа (по IP) остаётся боевым.
        builder.UseSetting("RateLimiting:GlobalPerMinute", "1000000");
        builder.UseSetting("Push:PrivateKey", Push.Keys.PrivateKey);
        builder.ConfigureTestServices(services =>
        {
            services.Replace(ServiceDescriptor.Singleton<IClock>(Clock));
            services.Replace(ServiceDescriptor.Singleton<IPushSender>(Push));
        });
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await base.DisposeAsync();
        await Task.WhenAll(_postgres.DisposeAsync().AsTask(), _redis.DisposeAsync().AsTask());
    }
}

[CollectionDefinition(Name)]
public sealed class ApiCollection : ICollectionFixture<MiseApiFactory>
{
    public const string Name = "api";
}

/// <summary>Отправитель пушей для тестов: запоминает, кому и что ушло; адреса с «gone» — «подписки больше нет».</summary>
public sealed class PushRecorder(VapidKeys keys) : IPushSender
{
    public VapidKeys Keys { get; } = keys;
    public ConcurrentQueue<(string Endpoint, PushMessage Message)> Sent { get; } = new();

    public bool IsConfigured => true;
    public string? PublicKey => Keys.PublicKey;

    public Task<PushSendResult> SendAsync(PushEndpoint target, PushMessage message, CancellationToken ct)
    {
        if (target.Endpoint.Contains("gone", StringComparison.Ordinal))
            return Task.FromResult(PushSendResult.Gone);
        Sent.Enqueue((target.Endpoint, message));
        return Task.FromResult(PushSendResult.Sent);
    }
}
