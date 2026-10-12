using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Mise.Infrastructure.Persistence;

/// <summary>Используется только `dotnet ef` для генерации миграций; к БД не подключается.</summary>
internal sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<MiseDbContext>
{
    public MiseDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<MiseDbContext>()
            .UseNpgsql("Host=localhost;Database=mise;Username=mise;Password=design-time-only",
                npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history"))
            .UseSnakeCaseNamingConvention()
            .Options;
        return new MiseDbContext(options);
    }
}
