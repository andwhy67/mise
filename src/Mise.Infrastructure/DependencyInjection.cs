using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Mise.Application.Abstractions;
using Mise.Domain.Abstractions;
using Mise.Infrastructure.Persistence;
using Mise.Application.Simulator;
using Mise.Infrastructure.Seed;
using Mise.Infrastructure.Simulator;
using Mise.Infrastructure.Time;
using StackExchange.Redis;

namespace Mise.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var postgres = configuration.GetConnectionString("Postgres")
            ?? throw new InvalidOperationException("ConnectionStrings:Postgres is not configured.");
        var redis = configuration.GetConnectionString("Redis")
            ?? throw new InvalidOperationException("ConnectionStrings:Redis is not configured.");

        services.AddSingleton<IClock, SystemClock>();

        services.AddDbContext<MiseDbContext>(options => options
            .UseNpgsql(postgres, npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history"))
            .UseSnakeCaseNamingConvention());
        services.AddScoped<IMiseDbContext>(sp => sp.GetRequiredService<MiseDbContext>());
        services.AddScoped<IStockLocks, PostgresStockLocks>();
        services.AddScoped<DatabaseSeeder>();
        services.AddScoped<DemoStockSeeder>();
        services.AddSingleton<ISimulatorControlStore, RedisSimulatorControlStore>();

        services.AddSingleton<IConnectionMultiplexer>(_ =>
        {
            var options = ConfigurationOptions.Parse(redis);
            options.AbortOnConnectFail = false;
            return ConnectionMultiplexer.Connect(options);
        });

        return services;
    }
}
