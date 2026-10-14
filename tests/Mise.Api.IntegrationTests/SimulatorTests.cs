using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Mise.Application.Simulator;
using Mise.Infrastructure.Seed;
using StackExchange.Redis;

namespace Mise.Api.IntegrationTests;

/// <summary>Управление симулятором через API/Redis и демо-наполнение склада.</summary>
[Collection(ApiCollection.Name)]
public sealed class SimulatorTests(MiseApiFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task Control_endpoints_change_desired_state_and_push_it()
    {
        await ResetSimulatorKeysAsync();
        await using var hub = await HubRecorder.ConnectAsync(factory);

        var initial = await _client.GetFromJsonAsync<JsonElement>("/api/v1/simulator/state");
        Assert.False(initial.GetProperty("online").GetBoolean());
        Assert.False(initial.GetProperty("control").GetProperty("running").GetBoolean());
        Assert.Equal(0, initial.GetProperty("control").GetProperty("version").GetInt64());

        var started = await PostAsync("/api/v1/simulator/start", new { scenario = "Chaos", speed = 20, seed = 42 });
        var control = started.GetProperty("control");
        Assert.True(control.GetProperty("running").GetBoolean());
        Assert.Equal("Chaos", control.GetProperty("scenario").GetString());
        Assert.Equal(20, control.GetProperty("speed").GetInt32());
        Assert.Equal(42, control.GetProperty("seed").GetInt32());
        Assert.Equal(1, control.GetProperty("version").GetInt64());
        await hub.WaitForAsync("SimulatorStateChanged", e => e.GetProperty("control").GetProperty("version").GetInt64() == 1);

        // Симулятор читает то же состояние из Redis по общему протоколу.
        var raw = await Redis.StringGetAsync(SimulatorProtocol.ControlKey);
        var stored = JsonSerializer.Deserialize<SimulatorControl>(raw.ToString(), SimulatorProtocol.Json)!;
        Assert.Equal(new SimulatorControl(true, SimulatorScenario.Chaos, 20, 42, 1, stored.UpdatedAt), stored);

        var tooFast = await _client.PostAsJsonAsync("/api/v1/simulator/speed", new { speed = 61 });
        await tooFast.EnsureStatusAsync(HttpStatusCode.BadRequest);
        Assert.True((await tooFast.ReadJsonAsync()).GetProperty("errors").TryGetProperty("speed", out _));
        var unknown = await _client.PostAsJsonAsync("/api/v1/simulator/scenario", new { scenario = "Bogus" });
        await unknown.EnsureStatusAsync(HttpStatusCode.BadRequest);

        var slower = await PostAsync("/api/v1/simulator/speed", new { speed = 5 });
        Assert.Equal(5, slower.GetProperty("control").GetProperty("speed").GetInt32());
        var scenario = await PostAsync("/api/v1/simulator/scenario", new { scenario = "SupplyDelay" });
        Assert.Equal("SupplyDelay", scenario.GetProperty("control").GetProperty("scenario").GetString());
        var stopped = await PostAsync("/api/v1/simulator/stop", null);
        Assert.False(stopped.GetProperty("control").GetProperty("running").GetBoolean());
        Assert.Equal(4, stopped.GetProperty("control").GetProperty("version").GetInt64());

        // Старт без тела — допустим, настройки сохраняются.
        var restarted = await PostAsync("/api/v1/simulator/start", null);
        Assert.Equal("SupplyDelay", restarted.GetProperty("control").GetProperty("scenario").GetString());
        Assert.Equal(5, restarted.GetProperty("control").GetProperty("speed").GetInt32());
    }

    [Fact]
    public async Task Heartbeat_from_simulator_makes_it_online_and_is_relayed_to_clients()
    {
        await ResetSimulatorKeysAsync();
        await using var hub = await HubRecorder.ConnectAsync(factory);
        var status = new SimulatorStatus("test-sim", true, SimulatorScenario.RushHour, 10, 7, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow, SimulatorCounters.Empty with { OrdersCreated = 12, OrdersRejected = 3 },
            [new SimulatorActivity(DateTimeOffset.UtcNow, "order", "ORD-000001: Карбонара")]);
        var json = JsonSerializer.Serialize(status, SimulatorProtocol.Json);

        await Redis.StringSetAsync(SimulatorProtocol.StatusKey, json, SimulatorProtocol.StatusTtl);
        await Redis.Multiplexer.GetSubscriber().PublishAsync(RedisChannel.Literal(SimulatorProtocol.StatusChannel), json);

        var state = await _client.GetFromJsonAsync<JsonElement>("/api/v1/simulator/state");
        Assert.True(state.GetProperty("online").GetBoolean());
        Assert.Equal(12, state.GetProperty("status").GetProperty("counters").GetProperty("ordersCreated").GetInt32());
        Assert.Equal("RushHour", state.GetProperty("status").GetProperty("scenario").GetString());
        await hub.WaitForAsync("SimulatorStateChanged", e => e.GetProperty("online").GetBoolean()
            && e.GetProperty("status").GetProperty("instanceId").GetString() == "test-sim");
    }

    [Fact]
    public async Task Demo_seed_creates_interesting_stock_once_per_day()
    {
        var first = await PostAsync("/api/v1/demo/seed-stock", null);
        factory.Clock.Advance(TimeSpan.FromMinutes(1)); // повтор позже — тело поставок другое (время), но это повтор
        var second = await PostAsync("/api/v1/demo/seed-stock", null);

        Assert.Equal(3, first.GetProperty("created").GetArrayLength());
        Assert.Equal(0, second.GetProperty("created").GetArrayLength());
        Assert.Equal(3, second.GetProperty("replayed").GetArrayLength());

        // Партии из демо-поставки «двухсуточной давности»: лосось — Warning, шпинат — Critical.
        var number = first.GetProperty("created")[1].GetString();
        var supplyId = await FindSupplyIdAsync(number!);
        var supply = await _client.GetFromJsonAsync<JsonElement>($"/api/v1/supplies/{supplyId}");
        var batchIds = supply.GetProperty("items").EnumerateArray()
            .ToDictionary(i => i.GetProperty("productId").GetGuid(), i => i.GetProperty("batchId").GetGuid());

        Assert.Equal("Warning", await ShelfStatusAsync(SeedData.Products.Salmon, batchIds[SeedData.Products.Salmon]));
        Assert.Equal("Warning", await ShelfStatusAsync(SeedData.Products.Cream, batchIds[SeedData.Products.Cream]));
        Assert.Equal("Critical", await ShelfStatusAsync(SeedData.Products.Spinach, batchIds[SeedData.Products.Spinach]));
    }

    private IDatabase Redis => factory.Services.GetRequiredService<IConnectionMultiplexer>().GetDatabase();

    private async Task ResetSimulatorKeysAsync() =>
        await Redis.KeyDeleteAsync([SimulatorProtocol.ControlKey, SimulatorProtocol.StatusKey]);

    private async Task<JsonElement> PostAsync(string path, object? body)
    {
        var response = body is null ? await _client.PostAsync(path, null) : await _client.PostAsJsonAsync(path, body);
        await response.EnsureStatusAsync(HttpStatusCode.OK);
        return await response.ReadJsonAsync();
    }

    private async Task<Guid> FindSupplyIdAsync(string number)
    {
        for (var page = 1; ; page++)
        {
            var items = (await _client.GetFromJsonAsync<JsonElement>($"/api/v1/supplies?page={page}&pageSize=100"))
                .GetProperty("items").EnumerateArray().ToList();
            if (items.Count == 0)
                throw new Xunit.Sdk.XunitException($"Поставка {number} не найдена");
            if (items.FirstOrDefault(s => s.GetProperty("number").GetString() == number) is { ValueKind: JsonValueKind.Object } found)
                return found.GetProperty("id").GetGuid();
        }
    }

    private async Task<string?> ShelfStatusAsync(Guid productId, Guid batchId)
    {
        var batches = await _client.GetFromJsonAsync<JsonElement>($"/api/v1/batches?productId={productId}&includeClosed=true");
        return batches.EnumerateArray().Single(b => b.GetProperty("id").GetGuid() == batchId)
            .GetProperty("shelfLife").GetProperty("status").GetString();
    }
}
