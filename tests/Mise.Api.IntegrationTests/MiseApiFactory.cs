using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Mise.Domain.Abstractions;
using Mise.Infrastructure.Time;
using Testcontainers.PostgreSql;
using Testcontainers.Redis;

namespace Mise.Api.IntegrationTests;

/// <summary>
/// Поднимает API поверх настоящих Postgres и Redis в контейнерах (один набор на коллекцию тестов).
/// Время управляемое (<see cref="Clock"/>), словарь засеян стартовыми продуктами и блюдами.
/// </summary>
public sealed class MiseApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public static readonly DateTimeOffset StartTime = new(2026, 10, 12, 8, 0, 0, TimeSpan.Zero);

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine")
        .WithDatabase("mise")
        .WithUsername("mise")
        .WithPassword("mise_test")
        .Build();

    private readonly RedisContainer _redis = new RedisBuilder("redis:7-alpine")
        .Build();

    public FakeClock Clock { get; } = new(StartTime);

    public async Task InitializeAsync()
    {
        await Task.WhenAll(_postgres.StartAsync(), _redis.StartAsync());
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Postgres", _postgres.GetConnectionString());
        builder.UseSetting("ConnectionStrings:Redis", _redis.GetConnectionString());
        builder.UseSetting("Database:MigrateOnStartup", "true");
        builder.UseSetting("Seed:Enabled", "true");
        builder.UseSetting("Cors:AllowedOrigins", "http://localhost:5173");
        builder.ConfigureTestServices(services => services.Replace(ServiceDescriptor.Singleton<IClock>(Clock)));
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
