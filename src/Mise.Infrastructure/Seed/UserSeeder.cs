using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Mise.Application.Auth;
using Mise.Application.Common;
using Mise.Domain.Abstractions;
using Mise.Domain.Users;
using Mise.Infrastructure.Persistence;

namespace Mise.Infrastructure.Seed;

/// <summary>
/// Стартовые учётные записи из env: демо-пользователи manager / chef / warehouse (Auth:DemoUsersPassword)
/// и служебный simulator (Auth:SimulatorPassword). Существующих не трогает — пароль, сменённый в системе, не перезаписывается.
/// </summary>
public sealed class UserSeeder(MiseDbContext db, IPasswordHasher hasher, IClock clock, IOptions<AuthOptions> options, ILogger<UserSeeder> logger)
{
    public const string SimulatorLogin = "simulator";

    public async Task SeedAsync(CancellationToken ct = default)
    {
        var settings = options.Value;
        var wanted = new List<(string Login, string DisplayName, UserRole Role, string Password)>();
        if (!string.IsNullOrWhiteSpace(settings.DemoUsersPassword))
            wanted.AddRange(DemoUsers.All.Select(u => (u.Login, u.DisplayName, u.Role, settings.DemoUsersPassword)));
        if (!string.IsNullOrWhiteSpace(settings.SimulatorPassword))
            wanted.Add((SimulatorLogin, "Симулятор", UserRole.Simulator, settings.SimulatorPassword));
        if (wanted.Count == 0)
            return;

        var logins = wanted.Select(w => w.Login).ToList();
        var existing = (await db.Users.Where(u => logins.Contains(u.Login)).Select(u => u.Login).ToListAsync(ct)).ToHashSet();
        var missing = wanted.Where(w => !existing.Contains(w.Login)).ToList();
        if (missing.Count == 0)
            return;

        foreach (var (login, displayName, role, password) in missing)
            db.Users.Add(User.Create(Guid.CreateVersion7(clock.UtcNow), login, displayName, role, hasher.Hash(password), clock.UtcNow));

        try
        {
            await db.SaveChangesAsync(ct);
            logger.LogInformation("Seeded users: {Logins}", string.Join(", ", missing.Select(m => m.Login)));
        }
        catch (UniqueConstraintViolationException)
        {
            // Параллельный инстанс успел раньше.
            db.ChangeTracker.Clear();
        }
    }
}
