using Npgsql;

namespace Mise.Infrastructure;

/// <summary>
/// Строки подключения в обоих видах: «ключ=значение» (compose, локально) и URL, как их выдают облака
/// (Render: <c>postgresql://user:pass@host/db</c>, <c>redis://host:6379</c>) — Npgsql и StackExchange.Redis URL не понимают.
/// </summary>
public static class ConnectionStrings
{
    public static string Postgres(string value)
    {
        // Только настоящий URL («схема://»): «redis:6379» .NET тоже считает URI со схемой «redis».
        if (!value.Contains("://") || !Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme is not ("postgres" or "postgresql"))
            return value;
        var (user, password) = Credentials(uri);
        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = uri.Host,
            Port = uri.IsDefaultPort || uri.Port <= 0 ? 5432 : uri.Port,
            Database = Uri.UnescapeDataString(uri.AbsolutePath.TrimStart('/')),
            Username = user,
            Password = password,
        };
        // Параметры из URL (?sslmode=require) — как есть.
        foreach (var pair in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = pair.Split('=', 2);
            if (parts.Length == 2 && parts[0].Equals("sslmode", StringComparison.OrdinalIgnoreCase))
                builder.SslMode = Enum.Parse<SslMode>(parts[1], ignoreCase: true);
        }
        return builder.ConnectionString;
    }

    public static string Redis(string value)
    {
        if (!value.Contains("://") || !Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme is not ("redis" or "rediss"))
            return value;
        var (user, password) = Credentials(uri);
        var parts = new List<string> { $"{uri.Host}:{(uri.Port > 0 ? uri.Port : 6379)}" };
        if (!string.IsNullOrEmpty(user) && user != "default")
            parts.Add($"user={user}");
        if (!string.IsNullOrEmpty(password))
            parts.Add($"password={password}");
        if (uri.Scheme == "rediss")
            parts.Add("ssl=true");
        return string.Join(',', parts);
    }

    private static (string? User, string? Password) Credentials(Uri uri)
    {
        if (string.IsNullOrEmpty(uri.UserInfo))
            return (null, null);
        var parts = uri.UserInfo.Split(':', 2);
        return (Uri.UnescapeDataString(parts[0]), parts.Length > 1 ? Uri.UnescapeDataString(parts[1]) : null);
    }
}
