using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR.Client;
using Mise.Domain.Users;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Mise.Api.IntegrationTests;

/// <summary>Вход, refresh-токены, права по ролям, лимиты, заголовки, аудит, пользователи, роль БД только на чтение.</summary>
[Collection(ApiCollection.Name)]
public sealed class SecurityTests(MiseApiFactory factory)
{
    private const string RefreshCookie = "mise_refresh";

    [Fact]
    public async Task Login_returns_access_token_and_http_only_refresh_cookie()
    {
        var client = factory.CreateClientAs(null);

        var response = await client.PostAsJsonAsync("/api/v1/auth/login", new { login = " Manager ", password = MiseApiFactory.DemoPassword });

        await response.EnsureStatusAsync(HttpStatusCode.OK);
        var body = await response.ReadJsonAsync();
        Assert.Equal("Manager", body.GetProperty("user").GetProperty("role").GetString());
        Assert.False(body.TryGetProperty("refreshToken", out _)); // refresh — только в cookie
        var cookie = response.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith(RefreshCookie + "="));
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("path=/api/v1/auth", cookie, StringComparison.OrdinalIgnoreCase);

        using var me = new HttpRequestMessage(HttpMethod.Get, "/api/v1/auth/me");
        me.Headers.Authorization = new("Bearer", body.GetProperty("accessToken").GetString());
        var meBody = await (await client.SendAsync(me)).ReadJsonAsync();
        Assert.Equal("manager", meBody.GetProperty("login").GetString());
    }

    [Fact]
    public async Task Wrong_password_and_unknown_login_look_the_same()
    {
        var client = factory.CreateClientAs(null);

        var wrong = await client.PostAsJsonAsync("/api/v1/auth/login", new { login = "chef", password = "nope-nope" });
        var unknown = await client.PostAsJsonAsync("/api/v1/auth/login", new { login = "ghost", password = "nope-nope" });

        await wrong.EnsureStatusAsync(HttpStatusCode.Unauthorized);
        await unknown.EnsureStatusAsync(HttpStatusCode.Unauthorized);
        var a = await wrong.ReadJsonAsync();
        var b = await unknown.ReadJsonAsync();
        Assert.Equal("auth.invalid_credentials", a.GetProperty("code").GetString());
        Assert.Equal(a.GetProperty("code").GetString(), b.GetProperty("code").GetString());
        Assert.Equal(a.GetProperty("detail").GetString(), b.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task Account_is_locked_after_repeated_failures_and_unlocks_later()
    {
        var login = await CreateUserAsync(UserRole.Chef, "Locked-Pass-1");
        var client = factory.CreateClientAs(null);

        for (var i = 1; i <= 5; i++)
        {
            var failed = await client.PostAsJsonAsync("/api/v1/auth/login", new { login, password = "wrong-password" });
            await failed.EnsureStatusAsync(HttpStatusCode.Unauthorized);
            Assert.Equal(i < 5 ? "auth.invalid_credentials" : "auth.locked", (await failed.ReadJsonAsync()).GetProperty("code").GetString());
        }

        var locked = await client.PostAsJsonAsync("/api/v1/auth/login", new { login, password = "Locked-Pass-1" });
        Assert.Equal("auth.locked", (await locked.ReadJsonAsync()).GetProperty("code").GetString());

        factory.Clock.Advance(TimeSpan.FromMinutes(5));
        var ok = await client.PostAsJsonAsync("/api/v1/auth/login", new { login, password = "Locked-Pass-1" });
        await ok.EnsureStatusAsync(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Refresh_rotates_and_reuse_of_an_old_token_revokes_the_session()
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        var first = await LoginCookieAsync(client, "warehouse", MiseApiFactory.DemoPassword);

        var rotated = await RefreshAsync(client, first);
        await rotated.EnsureStatusAsync(HttpStatusCode.OK);
        var second = CookieOf(rotated);
        Assert.NotEqual(first, second);
        Assert.Equal("warehouse", (await rotated.ReadJsonAsync()).GetProperty("user").GetProperty("login").GetString());

        // Сразу же — гонка вкладок: отказ, но сессия жива.
        var race = await RefreshAsync(client, first);
        Assert.Equal("auth.refresh_race", (await race.ReadJsonAsync()).GetProperty("code").GetString());
        var stillAlive = await RefreshAsync(client, second);
        await stillAlive.EnsureStatusAsync(HttpStatusCode.OK);
        var third = CookieOf(stillAlive);

        // Позже — повтор старого токена считается кражей: отзывается вся цепочка, включая действующий токен.
        factory.Clock.Advance(TimeSpan.FromSeconds(30));
        var reuse = await RefreshAsync(client, first);
        await reuse.EnsureStatusAsync(HttpStatusCode.Unauthorized);
        Assert.Equal("auth.refresh_invalid", (await reuse.ReadJsonAsync()).GetProperty("code").GetString());
        await (await RefreshAsync(client, third)).EnsureStatusAsync(HttpStatusCode.Unauthorized);

        var audit = await AuditAsync("auth.refresh_reuse");
        Assert.NotEmpty(audit);
    }

    [Fact]
    public async Task Logout_revokes_the_refresh_token()
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        var cookie = await LoginCookieAsync(client, "chef", MiseApiFactory.DemoPassword);

        using var logout = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/logout");
        logout.Headers.Add("Cookie", $"{RefreshCookie}={cookie}");
        await (await client.SendAsync(logout)).EnsureStatusAsync(HttpStatusCode.NoContent);

        await (await RefreshAsync(client, cookie)).EnsureStatusAsync(HttpStatusCode.Unauthorized);
    }

    [Theory]
    // Авторизация проверяется до валидации тела: 400 — доступ есть (тело пустое), 403 — нет.
    [InlineData(UserRole.Chef, "POST", "/api/v1/supplies", HttpStatusCode.Forbidden)]
    [InlineData(UserRole.Warehouse, "POST", "/api/v1/supplies", HttpStatusCode.BadRequest)]
    [InlineData(UserRole.Simulator, "POST", "/api/v1/supplies", HttpStatusCode.BadRequest)]
    [InlineData(UserRole.Warehouse, "POST", "/api/v1/orders", HttpStatusCode.Forbidden)]
    [InlineData(UserRole.Chef, "POST", "/api/v1/orders", HttpStatusCode.BadRequest)]
    [InlineData(UserRole.Simulator, "POST", "/api/v1/orders", HttpStatusCode.BadRequest)]
    [InlineData(UserRole.Chef, "POST", "/api/v1/products", HttpStatusCode.Forbidden)]
    [InlineData(UserRole.Simulator, "POST", "/api/v1/products", HttpStatusCode.Forbidden)]
    [InlineData(UserRole.Manager, "POST", "/api/v1/products", HttpStatusCode.BadRequest)]
    [InlineData(UserRole.Chef, "POST", "/api/v1/batches/00000000-0000-0000-0000-000000000001/write-off", HttpStatusCode.Forbidden)]
    [InlineData(UserRole.Warehouse, "POST", "/api/v1/batches/00000000-0000-0000-0000-000000000001/write-off", HttpStatusCode.BadRequest)]
    [InlineData(UserRole.Chef, "GET", "/api/v1/analytics/kpi", HttpStatusCode.OK)]
    [InlineData(UserRole.Chef, "GET", "/api/v1/analytics/writeoffs", HttpStatusCode.Forbidden)]
    [InlineData(UserRole.Warehouse, "GET", "/api/v1/analytics/kpi", HttpStatusCode.Forbidden)]
    [InlineData(UserRole.Warehouse, "GET", "/api/v1/supply-suggestions", HttpStatusCode.OK)]
    [InlineData(UserRole.Chef, "GET", "/api/v1/supply-suggestions", HttpStatusCode.Forbidden)]
    [InlineData(UserRole.Chef, "GET", "/api/v1/users", HttpStatusCode.Forbidden)]
    [InlineData(UserRole.Warehouse, "GET", "/api/v1/audit", HttpStatusCode.Forbidden)]
    [InlineData(UserRole.Chef, "POST", "/api/v1/simulator/stop", HttpStatusCode.Forbidden)]
    [InlineData(UserRole.Simulator, "POST", "/api/v1/alerts/00000000-0000-0000-0000-000000000001/ack", HttpStatusCode.Forbidden)]
    [InlineData(UserRole.Chef, "GET", "/api/v1/stock", HttpStatusCode.OK)]
    [InlineData(UserRole.Simulator, "GET", "/api/v1/dishes", HttpStatusCode.OK)]
    public async Task Roles_are_enforced(UserRole role, string method, string path, HttpStatusCode expected)
    {
        var client = factory.CreateClientAs(role);
        using var request = new HttpRequestMessage(new HttpMethod(method), path);
        if (method == "POST")
            request.Content = JsonContent.Create(new { });

        var response = await client.SendAsync(request);

        await response.EnsureStatusAsync(expected);
    }

    [Fact]
    public async Task Anonymous_requests_get_401_except_health_ping_and_login()
    {
        var client = factory.CreateClientAs(null);

        var stock = await client.GetAsync("/api/v1/stock");
        await stock.EnsureStatusAsync(HttpStatusCode.Unauthorized);
        Assert.Equal("application/problem+json", stock.Content.Headers.ContentType?.MediaType);
        await (await client.GetAsync("/health")).EnsureStatusAsync(HttpStatusCode.OK);
        await (await client.GetAsync("/api/v1/ping")).EnsureStatusAsync(HttpStatusCode.OK);

        var forged = factory.CreateClientAs(null);
        forged.DefaultRequestHeaders.Authorization = new("Bearer", "eyJhbGciOiJub25lIn0.eyJzdWIiOiJ4In0.");
        await (await forged.GetAsync("/api/v1/stock")).EnsureStatusAsync(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Hub_requires_a_token()
    {
        var connection = new HubConnectionBuilder()
            .WithUrl(new Uri(factory.Server.BaseAddress, "hubs/stock"), options =>
            {
                options.HttpMessageHandlerFactory = _ => factory.Server.CreateHandler();
                options.Transports = Microsoft.AspNetCore.Http.Connections.HttpTransportType.LongPolling;
            })
            .Build();

        var error = await Assert.ThrowsAsync<HttpRequestException>(() => connection.StartAsync());
        Assert.Equal(HttpStatusCode.Unauthorized, error.StatusCode);
    }

    [Fact]
    public async Task Login_is_rate_limited_per_ip()
    {
        var client = factory.CreateClientAs(null);
        HttpResponseMessage? last = null;

        for (var i = 0; i < 11; i++)
            last = await client.PostAsJsonAsync("/api/v1/auth/login", new { login = "nobody-" + i, password = "whatever1" });

        Assert.NotNull(last);
        await last.EnsureStatusAsync(HttpStatusCode.TooManyRequests);
        Assert.True(last.Headers.Contains("Retry-After"));
        Assert.Equal("rate_limited", (await last.ReadJsonAsync()).GetProperty("code").GetString());
        // Другой IP не затронут.
        var other = await factory.CreateClientAs(null).PostAsJsonAsync("/api/v1/auth/login", new { login = "nobody", password = "whatever1" });
        await other.EnsureStatusAsync(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Security_headers_are_set()
    {
        var response = await factory.CreateClientAs(null).GetAsync("/health");

        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("DENY", response.Headers.GetValues("X-Frame-Options").Single());
        Assert.Equal("no-referrer", response.Headers.GetValues("Referrer-Policy").Single());
        Assert.StartsWith("default-src 'none'", response.Headers.GetValues("Content-Security-Policy").Single());
    }

    [Fact]
    public async Task Critical_actions_and_failed_logins_are_audited_with_user_and_ip()
    {
        var client = factory.CreateClientAs(UserRole.Warehouse);
        var product = await factory.CreateClient().CreateProductAsync();
        var supply = await client.CreateSupplyAsync(new { number = TestApi.Unique("SUP"), items = new[] { new { productId = product, quantity = 10m } } });
        var supplyId = supply.GetProperty("id").GetGuid();
        await factory.CreateClientAs(null).PostAsJsonAsync("/api/v1/auth/login", new { login = "warehouse", password = "bad-password" });

        var entries = await AuditAsync("supply.create", $"&entityId={supplyId}");
        var entry = Assert.Single(entries);
        Assert.Equal("warehouse", entry.GetProperty("login").GetString());
        Assert.Equal("Supply", entry.GetProperty("entity").GetString());
        Assert.StartsWith("10.", entry.GetProperty("ip").GetString());
        Assert.Equal("POST", entry.GetProperty("details").GetProperty("method").GetString());

        var failed = await AuditAsync("auth.login_failed");
        Assert.Contains(failed, e => e.GetProperty("login").GetString() == "warehouse"
            && e.GetProperty("details").GetProperty("reason").GetString() == "wrong_password");
    }

    [Fact]
    public async Task Manager_manages_users_and_access_changes_take_effect()
    {
        var manager = factory.CreateClient();
        var login = await CreateUserAsync(UserRole.Chef, "Initial-Pass-1");
        var duplicate = await manager.PostAsJsonAsync("/api/v1/users", new { login, displayName = "Дубль", role = "Chef", password = "Another-Pass-1" });
        await duplicate.EnsureStatusAsync(HttpStatusCode.Conflict);
        var weak = await manager.PostAsJsonAsync("/api/v1/users", new { login = TestApi.Unique("u").ToLowerInvariant(), displayName = "x", role = "Chef", password = "123" });
        await weak.EnsureStatusAsync(HttpStatusCode.BadRequest);

        var users = await manager.GetFromJsonAsync<JsonElement>("/api/v1/users");
        var id = users.EnumerateArray().Single(u => u.GetProperty("login").GetString() == login).GetProperty("id").GetGuid();

        var disable = await manager.PutAsJsonAsync($"/api/v1/users/{id}", new { displayName = "Повар", role = "Chef", isActive = false });
        await disable.EnsureStatusAsync(HttpStatusCode.OK);
        var inactive = await factory.CreateClientAs(null).PostAsJsonAsync("/api/v1/auth/login", new { login, password = "Initial-Pass-1" });
        Assert.Equal("auth.inactive", (await inactive.ReadJsonAsync()).GetProperty("code").GetString());

        await (await manager.PutAsJsonAsync($"/api/v1/users/{id}", new { displayName = "Повар", role = "Chef", isActive = true })).EnsureStatusAsync(HttpStatusCode.OK);
        await (await manager.PostAsJsonAsync($"/api/v1/users/{id}/password", new { password = "Changed-Pass-2" })).EnsureStatusAsync(HttpStatusCode.NoContent);
        var oldPassword = await factory.CreateClientAs(null).PostAsJsonAsync("/api/v1/auth/login", new { login, password = "Initial-Pass-1" });
        await oldPassword.EnsureStatusAsync(HttpStatusCode.Unauthorized);
        var newPassword = await factory.CreateClientAs(null).PostAsJsonAsync("/api/v1/auth/login", new { login, password = "Changed-Pass-2" });
        await newPassword.EnsureStatusAsync(HttpStatusCode.OK);

        Assert.NotEmpty(await AuditAsync("user.update", $"&entityId={id}"));
    }

    [Fact]
    public async Task Last_active_manager_cannot_be_demoted()
    {
        var manager = factory.CreateClient();
        var users = await manager.GetFromJsonAsync<JsonElement>("/api/v1/users");
        var managers = users.EnumerateArray()
            .Where(u => u.GetProperty("role").GetString() == "Manager" && u.GetProperty("isActive").GetBoolean())
            .ToList();
        Assert.Single(managers); // в тестах менеджер только демо-учётка
        var id = managers[0].GetProperty("id").GetGuid();

        var demote = await manager.PutAsJsonAsync($"/api/v1/users/{id}", new { displayName = "Менеджер", role = "Chef", isActive = true });

        await demote.EnsureStatusAsync(HttpStatusCode.Conflict);
        Assert.Equal("user.last_manager", (await demote.ReadJsonAsync()).GetProperty("code").GetString());
    }

    [Fact]
    public async Task Read_only_database_role_can_select_but_not_write_or_read_secrets()
    {
        _ = factory.Server; // запустить хост: роль создаёт инициализатор БД при старте
        var builder = new NpgsqlConnectionStringBuilder(factory.PostgresConnectionString)
        {
            Username = MiseApiFactory.ReadOnlyUser,
            Password = MiseApiFactory.ReadOnlyPassword,
        };
        await using var connection = new NpgsqlConnection(builder.ConnectionString);
        await using (var admin = new NpgsqlConnection(factory.PostgresConnectionString))
        {
            await admin.OpenAsync();
            await using var check = new NpgsqlCommand("SELECT count(*) FROM pg_roles WHERE rolname = 'mise_readonly'", admin);
            Assert.Equal(1L, await check.ExecuteScalarAsync()); // роль создал API при старте
        }
        await connection.OpenAsync();

        await using (var select = new NpgsqlCommand("SELECT count(*) FROM products", connection))
            Assert.True((long)(await select.ExecuteScalarAsync())! > 0);

        await using var insert = new NpgsqlCommand("DELETE FROM products", connection);
        var denied = await Assert.ThrowsAsync<PostgresException>(() => insert.ExecuteNonQueryAsync());
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, denied.SqlState);

        await using var secrets = new NpgsqlCommand("SELECT password_hash FROM users", connection);
        var hidden = await Assert.ThrowsAsync<PostgresException>(() => secrets.ExecuteScalarAsync());
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, hidden.SqlState);
    }

    private async Task<string> CreateUserAsync(UserRole role, string password)
    {
        var login = TestApi.Unique("user").ToLowerInvariant();
        var response = await factory.CreateClient().PostAsJsonAsync("/api/v1/users", new { login, displayName = "Тестовый", role = role.ToString(), password });
        await response.EnsureStatusAsync(HttpStatusCode.Created);
        return login;
    }

    private static async Task<string> LoginCookieAsync(HttpClient client, string login, string password)
    {
        var response = await client.PostAsJsonAsync("/api/v1/auth/login", new { login, password });
        await response.EnsureStatusAsync(HttpStatusCode.OK);
        return CookieOf(response);
    }

    private static async Task<HttpResponseMessage> RefreshAsync(HttpClient client, string cookie)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/refresh");
        request.Headers.Add("Cookie", $"{RefreshCookie}={cookie}");
        return await client.SendAsync(request);
    }

    private static string CookieOf(HttpResponseMessage response)
    {
        var header = response.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith(RefreshCookie + "="));
        return header[(RefreshCookie.Length + 1)..header.IndexOf(';')];
    }

    private async Task<List<JsonElement>> AuditAsync(string action, string filter = "") =>
        (await factory.CreateClient().GetFromJsonAsync<JsonElement>($"/api/v1/audit?action={action}{filter}&pageSize=100"))
            .GetProperty("items").EnumerateArray().ToList();
}
