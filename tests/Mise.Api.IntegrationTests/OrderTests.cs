using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Mise.Infrastructure.Persistence;

namespace Mise.Api.IntegrationTests;

[Collection(ApiCollection.Name)]
public sealed class OrderTests(MiseApiFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task Successful_order_consumes_by_fefo_across_batches()
    {
        var product = await _client.CreateProductAsync();
        var late = await _client.ReceiveAsync(product, 500m, shelfLifeHours: 48);
        var early = await _client.ReceiveAsync(product, 300m, shelfLifeHours: 5);
        var dish = await _client.CreateDishAsync((product, 200m));

        var order = await _client.CreateOrderAsync((dish, 2));

        Assert.StartsWith("ORD-", order.GetProperty("number").GetString());
        Assert.Equal("Created", order.GetProperty("status").GetString());
        var consumption = order.GetProperty("consumption").EnumerateArray()
            .ToDictionary(c => c.GetProperty("batchId").GetGuid(), c => c.GetProperty("consumed").GetDecimal());
        Assert.Equal(new Dictionary<Guid, decimal> { [early] = 300m, [late] = 100m }, consumption);
        Assert.Equal(400m, await _client.UsableAsync(product));

        var batches = (await _client.GetFromJsonAsync<JsonElement>($"/api/v1/stock/{product}/batches?includeClosed=true"))
            .GetProperty("batches").EnumerateArray().ToDictionary(b => b.GetProperty("id").GetGuid());
        Assert.Equal("Depleted", batches[early].GetProperty("status").GetString());
        Assert.Equal(400m, batches[late].GetProperty("remainingQty").GetDecimal());
    }

    [Fact]
    public async Task Shortage_is_rejected_with_missing_list_and_nothing_is_consumed()
    {
        var short1 = await _client.CreateProductAsync();
        var plenty = await _client.CreateProductAsync();
        await _client.ReceiveAsync(short1, 150m);
        await _client.ReceiveAsync(plenty, 10_000m);
        var dish = await _client.CreateDishAsync((short1, 100m), (plenty, 10m));

        var response = await _client.PostOrderAsync(null, (dish, 2));

        await response.EnsureStatusAsync(HttpStatusCode.UnprocessableEntity);
        var problem = await response.ReadJsonAsync();
        Assert.Equal("order.insufficient_stock", problem.GetProperty("code").GetString());
        var missing = problem.GetProperty("missing").EnumerateArray().Single();
        Assert.Equal(short1, missing.GetProperty("productId").GetGuid());
        Assert.Equal(200m, missing.GetProperty("required").GetDecimal());
        Assert.Equal(150m, missing.GetProperty("available").GetDecimal());
        Assert.Equal(50m, missing.GetProperty("shortage").GetDecimal());
        Assert.Single(missing.GetProperty("dishes").EnumerateArray());

        Assert.Equal(150m, await _client.UsableAsync(short1));
        Assert.Equal(10_000m, await _client.UsableAsync(plenty));
    }

    [Fact]
    public async Task Shared_ingredient_is_checked_for_the_whole_order()
    {
        var cream = await _client.CreateProductAsync("Milliliter");
        await _client.ReceiveAsync(cream, 150m);
        var carbonara = await _client.CreateDishAsync((cream, 50m));
        var salmonPasta = await _client.CreateDishAsync((cream, 100m));

        var together = await _client.PostOrderAsync(null, (carbonara, 2), (salmonPasta, 1));

        await together.EnsureStatusAsync(HttpStatusCode.UnprocessableEntity);
        var missing = (await together.ReadJsonAsync()).GetProperty("missing")[0];
        Assert.Equal(200m, missing.GetProperty("required").GetDecimal());
        Assert.Equal(2, missing.GetProperty("dishes").GetArrayLength());

        await _client.CreateOrderAsync((carbonara, 1), (salmonPasta, 1));
        Assert.Equal(0m, await _client.UsableAsync(cream));
    }

    [Fact]
    public async Task Expired_batch_is_not_used_for_orders()
    {
        var product = await _client.CreateProductAsync();
        await _client.ReceiveAsync(product, 1000m, shelfLifeHours: 24, createdAt: factory.Clock.UtcNow.AddDays(-2));
        await _client.ReceiveAsync(product, 100m);
        var dish = await _client.CreateDishAsync((product, 150m));

        var response = await _client.PostOrderAsync(null, (dish, 1));

        await response.EnsureStatusAsync(HttpStatusCode.UnprocessableEntity);
        Assert.Equal(100m, (await response.ReadJsonAsync()).GetProperty("missing")[0].GetProperty("available").GetDecimal());
    }

    [Fact]
    public async Task Dish_enters_stop_list_when_stock_runs_out_and_leaves_after_supply()
    {
        var product = await _client.CreateProductAsync();
        await _client.ReceiveAsync(product, 200m);
        var dish = await _client.CreateDishAsync((product, 100m));

        Assert.Equal(2, await MaxPortionsAsync(dish));
        await _client.CreateOrderAsync((dish, 2));

        var stop = await StopListItemAsync(dish);
        Assert.NotNull(stop);
        Assert.Equal("Shortage", stop.Value.GetProperty("reason").GetString());
        Assert.Equal(product, stop.Value.GetProperty("limitingProduct").GetProperty("productId").GetGuid());
        Assert.Equal(factory.Clock.UtcNow, stop.Value.GetProperty("since").GetDateTimeOffset());
        Assert.Equal(0, await MaxPortionsAsync(dish));

        await _client.ReceiveAsync(product, 500m);

        Assert.Null(await StopListItemAsync(dish));
        Assert.Equal(5, await MaxPortionsAsync(dish));
        var history = (await _client.GetFromJsonAsync<JsonElement>($"/api/v1/stoplist/history?dishId={dish}"))
            .GetProperty("items").EnumerateArray().ToList();
        var interval = Assert.Single(history);
        Assert.Equal("Shortage", interval.GetProperty("reason").GetString());
        Assert.Equal(product, interval.GetProperty("limitingProductId").GetGuid());
        Assert.NotEqual(JsonValueKind.Null, interval.GetProperty("endedAt").ValueKind);
    }

    [Fact]
    public async Task Manually_disabled_dish_is_in_stop_list_and_cannot_be_ordered()
    {
        var product = await _client.CreateProductAsync();
        await _client.ReceiveAsync(product, 1000m);
        var dish = await _client.CreateDishAsync((product, 10m));

        var update = await _client.PutAsJsonAsync($"/api/v1/dishes/{dish}", new { name = TestApi.Unique("Выкл"), price = 1m, isActive = false });
        await update.EnsureStatusAsync(HttpStatusCode.OK);

        var stop = await StopListItemAsync(dish);
        Assert.Equal("Manual", stop!.Value.GetProperty("reason").GetString());
        var response = await _client.PostOrderAsync(null, (dish, 1));
        await response.EnsureStatusAsync(HttpStatusCode.UnprocessableEntity);
        Assert.Equal("order.dish_unavailable", (await response.ReadJsonAsync()).GetProperty("code").GetString());
        Assert.Equal(1000m, await _client.UsableAsync(product));
    }

    [Fact]
    public async Task Cancel_returns_products_to_the_same_batches()
    {
        var product = await _client.CreateProductAsync();
        var batch = await _client.ReceiveAsync(product, 500m);
        var dish = await _client.CreateDishAsync((product, 120m));
        var orderId = (await _client.CreateOrderAsync((dish, 3))).GetProperty("id").GetGuid();
        Assert.Equal(140m, await _client.UsableAsync(product));

        var cancel = await _client.PostAsync($"/api/v1/orders/{orderId}/cancel", null);

        await cancel.EnsureStatusAsync(HttpStatusCode.OK);
        var result = await cancel.ReadJsonAsync();
        Assert.Equal("Cancelled", result.GetProperty("order").GetProperty("status").GetString());
        var returned = Assert.Single(result.GetProperty("returned").EnumerateArray());
        Assert.Equal(batch, returned.GetProperty("batchId").GetGuid());
        Assert.Equal(360m, returned.GetProperty("quantity").GetDecimal());
        Assert.Empty(result.GetProperty("lost").EnumerateArray());
        Assert.Equal(500m, await _client.UsableAsync(product));

        var again = await _client.PostAsync($"/api/v1/orders/{orderId}/cancel", null);
        await again.EnsureStatusAsync(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Cancel_after_batch_expired_counts_as_loss()
    {
        var product = await _client.CreateProductAsync();
        await _client.ReceiveAsync(product, 100m, shelfLifeHours: 2);
        var dish = await _client.CreateDishAsync((product, 40m));
        var orderId = (await _client.CreateOrderAsync((dish, 1))).GetProperty("id").GetGuid();

        var start = factory.Clock.UtcNow;
        try
        {
            factory.Clock.Advance(TimeSpan.FromHours(3));
            var cancel = await _client.PostAsync($"/api/v1/orders/{orderId}/cancel", null);

            await cancel.EnsureStatusAsync(HttpStatusCode.OK);
            var result = await cancel.ReadJsonAsync();
            Assert.Empty(result.GetProperty("returned").EnumerateArray());
            Assert.Equal(40m, Assert.Single(result.GetProperty("lost").EnumerateArray()).GetProperty("quantity").GetDecimal());
            Assert.Equal(0m, await _client.UsableAsync(product));
        }
        finally
        {
            factory.Clock.Set(start);
        }
    }

    [Fact]
    public async Task Status_moves_forward_only_and_done_order_cannot_be_cancelled()
    {
        var product = await _client.CreateProductAsync();
        await _client.ReceiveAsync(product, 100m);
        var dish = await _client.CreateDishAsync((product, 10m));
        var id = (await _client.CreateOrderAsync((dish, 1))).GetProperty("id").GetGuid();

        await (await _client.PatchAsJsonAsync($"/api/v1/orders/{id}/status", new { status = "Cooking" })).EnsureStatusAsync(HttpStatusCode.OK);
        await (await _client.PatchAsJsonAsync($"/api/v1/orders/{id}/status", new { status = "Done" })).EnsureStatusAsync(HttpStatusCode.OK);
        var back = await _client.PatchAsJsonAsync($"/api/v1/orders/{id}/status", new { status = "Cooking" });
        var cancel = await _client.PostAsync($"/api/v1/orders/{id}/cancel", null);

        await back.EnsureStatusAsync(HttpStatusCode.UnprocessableEntity);
        Assert.Equal("order.invalid_transition", (await back.ReadJsonAsync()).GetProperty("code").GetString());
        await cancel.EnsureStatusAsync(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Repeated_order_with_same_key_is_not_consumed_twice()
    {
        var product = await _client.CreateProductAsync();
        await _client.ReceiveAsync(product, 100m);
        var dish = await _client.CreateDishAsync((product, 10m));
        var key = Guid.NewGuid().ToString();

        var first = await _client.PostOrderAsync(key, (dish, 2));
        var second = await _client.PostOrderAsync(key, (dish, 2));
        var otherBody = await _client.PostOrderAsync(key, (dish, 3));

        await first.EnsureStatusAsync(HttpStatusCode.Created);
        await second.EnsureStatusAsync(HttpStatusCode.OK);
        await otherBody.EnsureStatusAsync(HttpStatusCode.Conflict);
        Assert.Equal((await first.ReadJsonAsync()).GetProperty("id").GetGuid(), (await second.ReadJsonAsync()).GetProperty("id").GetGuid());
        Assert.Equal(80m, await _client.UsableAsync(product));
    }

    [Fact]
    public async Task Fifty_parallel_orders_never_oversell_limited_stock()
    {
        var product = await _client.CreateProductAsync();
        // Запас ровно на 10 порций, разбитый на несколько партий с разными сроками.
        foreach (var (qty, hours) in new[] { (300m, 10), (300m, 20), (400m, 30) })
            await _client.ReceiveAsync(product, qty, shelfLifeHours: hours);
        var dish = await _client.CreateDishAsync((product, 100m));

        var responses = await Task.WhenAll(Enumerable.Range(0, 50)
            .Select(_ => factory.CreateClient().PostOrderAsync(null, (dish, 1))));

        Assert.Equal(10, responses.Count(r => r.StatusCode == HttpStatusCode.Created));
        Assert.Equal(40, responses.Count(r => r.StatusCode == HttpStatusCode.UnprocessableEntity));
        Assert.Equal(0m, await _client.UsableAsync(product));
        await AssertLedgerConsistentAsync(product);
    }

    [Fact]
    public async Task Parallel_orders_with_overlapping_products_do_not_deadlock()
    {
        var a = await _client.CreateProductAsync();
        var b = await _client.CreateProductAsync();
        var c = await _client.CreateProductAsync();
        foreach (var p in new[] { a, b, c })
        {
            await _client.ReceiveAsync(p, 1500m, shelfLifeHours: 12);
            await _client.ReceiveAsync(p, 1500m, shelfLifeHours: 36);
        }
        var ab = await _client.CreateDishAsync((a, 10m), (b, 10m));
        var bc = await _client.CreateDishAsync((b, 10m), (c, 10m));
        var ca = await _client.CreateDishAsync((c, 10m), (a, 10m));
        var dishes = new[] { ab, bc, ca };

        var responses = await Task.WhenAll(Enumerable.Range(0, 60)
            .Select(i => factory.CreateClient().PostOrderAsync(null, (dishes[i % 3], 1), (dishes[(i + 1) % 3], 2))));

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.Created, r.StatusCode));
        // Каждый заказ — 3 порции, в каждой 2 продукта по 10: 60 заказов × 3 × 2 × 10 = 3600 списано всего.
        var used = 3000m * 3 - (await _client.UsableAsync(a) + await _client.UsableAsync(b) + await _client.UsableAsync(c));
        Assert.Equal(3600m, used);
        foreach (var p in new[] { a, b, c })
            await AssertLedgerConsistentAsync(p);
    }

    [Fact]
    public async Task Menu_availability_reports_max_portions_and_limiting_product()
    {
        var pasta = await _client.CreateProductAsync();
        var cream = await _client.CreateProductAsync("Milliliter");
        await _client.ReceiveAsync(pasta, 1000m);
        await _client.ReceiveAsync(cream, 175m);
        var dish = await _client.CreateDishAsync((pasta, 120m), (cream, 50m));

        var item = await AvailabilityAsync(dish);

        Assert.Equal(3, item.GetProperty("maxPortions").GetInt32());
        Assert.Equal("Available", item.GetProperty("status").GetString());
        Assert.Equal(cream, item.GetProperty("limitingProduct").GetProperty("productId").GetGuid());
    }

    private async Task AssertLedgerConsistentAsync(Guid productId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        using var demo = factory.AsDemoRestaurant();
        var db = scope.ServiceProvider.GetRequiredService<MiseDbContext>();
        var batches = await db.Batches.AsNoTracking().Where(b => b.ProductId == productId).ToListAsync();
        var ledger = await db.StockMovements.AsNoTracking().Where(m => m.ProductId == productId)
            .GroupBy(m => m.BatchId).Select(g => new { g.Key, Sum = g.Sum(m => m.QtyDelta) }).ToDictionaryAsync(x => x.Key, x => x.Sum);

        Assert.All(batches, b =>
        {
            Assert.True(b.RemainingQty >= 0, $"Отрицательный остаток в партии {b.Id}");
            Assert.Equal(ledger[b.Id], b.RemainingQty);
        });
    }

    private async Task<int> MaxPortionsAsync(Guid dishId) => (await AvailabilityAsync(dishId)).GetProperty("maxPortions").GetInt32();

    private async Task<JsonElement> AvailabilityAsync(Guid dishId) =>
        (await _client.GetFromJsonAsync<JsonElement>("/api/v1/menu/availability"))
            .EnumerateArray().Single(d => d.GetProperty("dishId").GetGuid() == dishId);

    private async Task<JsonElement?> StopListItemAsync(Guid dishId) =>
        (await _client.GetFromJsonAsync<JsonElement>("/api/v1/stoplist"))
            .EnumerateArray().Cast<JsonElement?>().SingleOrDefault(d => d!.Value.GetProperty("dishId").GetGuid() == dishId);
}
