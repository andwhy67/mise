using Microsoft.EntityFrameworkCore;

namespace Mise.Infrastructure.Persistence;

public sealed class MiseDbContext(DbContextOptions<MiseDbContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(MiseDbContext).Assembly);
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // Количества: numeric(14,3) по умолчанию для всех decimal (PLAN §2.2).
        configurationBuilder.Properties<decimal>().HavePrecision(14, 3);
    }
}
