using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Mise.Infrastructure.Persistence;

namespace Mise.Api.IntegrationTests;

[Collection(ApiCollection.Name)]
public sealed class SystemEndpointsTests(MiseApiFactory factory)
{
    [Fact]
    public async Task Health_is_healthy_with_postgres_and_redis()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Healthy", body.GetProperty("status").GetString());
        var checks = body.GetProperty("checks");
        Assert.Equal("Healthy", checks.GetProperty("postgres").GetProperty("status").GetString());
        Assert.Equal("Healthy", checks.GetProperty("redis").GetProperty("status").GetString());
    }

    [Fact]
    public async Task Migrations_are_applied_on_startup()
    {
        _ = factory.CreateClient();
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MiseDbContext>();

        var pending = await db.Database.GetPendingMigrationsAsync();
        var applied = await db.Database.GetAppliedMigrationsAsync();

        Assert.Empty(pending);
        Assert.Contains(applied, m => m.EndsWith("_Init", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Ping_returns_ok_with_utc_server_time()
    {
        var client = factory.CreateClient();

        var body = await client.GetFromJsonAsync<JsonElement>("/api/v1/ping");

        Assert.Equal("ok", body.GetProperty("status").GetString());
        Assert.Equal(TimeSpan.Zero, body.GetProperty("serverTimeUtc").GetDateTimeOffset().Offset);
    }

    [Fact]
    public async Task Unknown_route_returns_problem_details()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync("/api/v1/does-not-exist");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Theory]
    [InlineData("{\"number\":", "request.malformed")]
    [InlineData("{\"number\":\"X\",\"items\":[{\"productId\":\"10000000-0000-0000-0000-000000000001\",\"quantity\":1,\"unit\":\"Bucket\"}]}", "$.items[0].unit")]
    public async Task Malformed_body_returns_problem_details_with_hint(string body, string expectedFragment)
    {
        var client = factory.CreateClient();

        var response = await client.PostAsync("/api/v1/supplies", new StringContent(body, System.Text.Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains(expectedFragment, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Correlation_id_is_echoed_back()
    {
        var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/ping");
        request.Headers.Add("X-Correlation-ID", "demo-123");

        var response = await client.SendAsync(request);

        Assert.Equal("demo-123", response.Headers.GetValues("X-Correlation-ID").Single());
    }

    [Fact]
    public async Task Cors_allows_configured_origin_only()
    {
        var client = factory.CreateClient();

        var allowed = await SendPreflight(client, "http://localhost:5173");
        var denied = await SendPreflight(client, "http://evil.example");

        Assert.Equal("http://localhost:5173", allowed.Headers.GetValues("Access-Control-Allow-Origin").Single());
        Assert.False(denied.Headers.Contains("Access-Control-Allow-Origin"));
    }

    private static Task<HttpResponseMessage> SendPreflight(HttpClient client, string origin)
    {
        var request = new HttpRequestMessage(HttpMethod.Options, "/api/v1/ping");
        request.Headers.Add("Origin", origin);
        request.Headers.Add("Access-Control-Request-Method", "GET");
        return client.SendAsync(request);
    }
}
