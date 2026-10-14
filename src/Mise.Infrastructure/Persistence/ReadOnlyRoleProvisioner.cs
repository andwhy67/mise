using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Mise.Infrastructure.Persistence;

/// <summary>
/// Роль БД только на чтение для Grafana и отчётов (Database:ReadOnlyUser / Database:ReadOnlyPassword).
/// Идемпотентно: создать или обновить пароль, выдать SELECT на все таблицы (и будущие), кроме секретов (users, refresh_tokens).
/// Имя и пароль передаются параметрами во временную функцию — без склейки SQL. Без права CREATEROLE шаг пропускается с предупреждением.
/// </summary>
public sealed partial class ReadOnlyRoleProvisioner(MiseDbContext db, IConfiguration configuration, ILogger<ReadOnlyRoleProvisioner> logger)
{
    // Константа без пользовательских данных; значения приходят параметрами функции и экранируются format(%I, %L).
    private const string CreateFunctionSql = """
        CREATE OR REPLACE FUNCTION pg_temp.mise_ensure_readonly(role_name text, role_password text) RETURNS void
        LANGUAGE plpgsql AS $fn$
        BEGIN
            IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = role_name) THEN
                EXECUTE format('ALTER ROLE %I WITH LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE PASSWORD %L', role_name, role_password);
            ELSE
                EXECUTE format('CREATE ROLE %I WITH LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE PASSWORD %L', role_name, role_password);
            END IF;
            EXECUTE format('GRANT CONNECT ON DATABASE %I TO %I', current_database(), role_name);
            EXECUTE format('GRANT USAGE ON SCHEMA public TO %I', role_name);
            EXECUTE format('GRANT SELECT ON ALL TABLES IN SCHEMA public TO %I', role_name);
            EXECUTE format('ALTER DEFAULT PRIVILEGES IN SCHEMA public GRANT SELECT ON TABLES TO %I', role_name);
            EXECUTE format('REVOKE ALL ON users, refresh_tokens FROM %I', role_name);
        END
        $fn$;
        """;

    public async Task EnsureAsync(CancellationToken ct = default)
    {
        var role = configuration["Database:ReadOnlyUser"];
        var password = configuration["Database:ReadOnlyPassword"];
        if (string.IsNullOrWhiteSpace(role) || string.IsNullOrWhiteSpace(password))
            return;
        if (!RoleName().IsMatch(role))
        {
            logger.LogWarning("Database:ReadOnlyUser {Role}: недопустимое имя роли, пропускаю", role);
            return;
        }

        try
        {
            // Временная функция живёт в сессии — обе команды на одном открытом соединении.
            await db.Database.OpenConnectionAsync(ct);
            try
            {
                await db.Database.ExecuteSqlRawAsync(CreateFunctionSql, ct);
                await db.Database.ExecuteSqlAsync($"SELECT pg_temp.mise_ensure_readonly({role}, {password})", ct);
            }
            finally
            {
                await db.Database.CloseConnectionAsync();
            }
            logger.LogInformation("Read-only database role {Role} is ready", role);
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.InsufficientPrivilege)
        {
            logger.LogWarning("Нет прав на создание роли {Role}: Grafana использует учётку из своей конфигурации", role);
        }
    }

    [GeneratedRegex("^[a-z_][a-z0-9_]{2,62}$")]
    private static partial Regex RoleName();
}
