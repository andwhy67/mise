using Microsoft.EntityFrameworkCore;
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
        await services.GetRequiredService<MiseDbContext>().Database.MigrateAsync(cancellationToken);
        logger.LogInformation("Database migrations applied");

        if (configuration.GetValue("Seed:Enabled", false))
            await services.GetRequiredService<DatabaseSeeder>().SeedAsync(cancellationToken);
        await services.GetRequiredService<UserSeeder>().SeedAsync(cancellationToken);
        await services.GetRequiredService<ReadOnlyRoleProvisioner>().EnsureAsync(cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
