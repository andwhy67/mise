using Microsoft.EntityFrameworkCore;
using Mise.Application.Abstractions;
using Mise.Application.Common;
using Mise.Domain.Abstractions;
using Mise.Domain.Restaurants;
using Mise.Infrastructure.Persistence;
using Mise.Infrastructure.Seed;

namespace Mise.Api.Infrastructure;

/// <summary>
/// Подготовка БД при старте: миграции (Database:MigrateOnStartup), стартовый словарь (Seed:Enabled), учётные записи
/// из env и роль только на чтение. Это первый IHostedService: хост дожидается его StartAsync до запуска фоновых задач
/// и HTTP-сервера — ни один запрос (и ни один тест) не увидит БД до окончания инициализации.
/// </summary>
internal sealed class DatabaseInitializer(IServiceScopeFactory scopes, IConfiguration configuration, ILogger<DatabaseInitializer> logger)
    : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!configuration.GetValue("Database:MigrateOnStartup", false))
            return;

        await using var scope = scopes.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var db = services.GetRequiredService<MiseDbContext>();
        await db.Database.MigrateAsync(cancellationToken);
        logger.LogInformation("Database migrations applied");

        // Демо-ресторан — дом демо-учёток, стартового словаря и симулятора; словарь и учётки сеются в нём.
        await EnsureDemoRestaurantAsync(db, services.GetRequiredService<IClock>(), cancellationToken);
        using (services.GetRequiredService<ITenant>().Use(SeedData.DemoRestaurantId))
        {
            if (configuration.GetValue("Seed:Enabled", false))
                await services.GetRequiredService<DatabaseSeeder>().SeedAsync(cancellationToken);
            await services.GetRequiredService<UserSeeder>().SeedAsync(cancellationToken);
        }
        await services.GetRequiredService<ReadOnlyRoleProvisioner>().EnsureAsync(cancellationToken);
    }

    private static async Task EnsureDemoRestaurantAsync(MiseDbContext db, IClock clock, CancellationToken ct)
    {
        if (await db.Restaurants.AnyAsync(r => r.Id == SeedData.DemoRestaurantId, ct))
            return;
        db.Restaurants.Add(new Restaurant(SeedData.DemoRestaurantId, SeedData.DemoRestaurantName, clock.UtcNow, isDemo: true));
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (UniqueConstraintViolationException)
        {
            // Параллельный инстанс успел раньше.
            db.ChangeTracker.Clear();
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
