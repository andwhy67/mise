using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Mise.Infrastructure.Persistence;

namespace Mise.Api.IntegrationTests;

[Collection(ApiCollection.Name)]
public sealed class SupplyAndStockTests(MiseApiFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();
    private DateTimeOffset Now => factory.Clock.UtcNow;

    [Fact]
    public async Task Each_item_becomes_a_batch_with_expiry_from_the_right_source()
    {
        var cream = await _client.CreateProductAsync("Milliliter", defaultShelfLifeHours: 48);
        var salmon = await _client.CreateProductAsync(defaultShelfLifeHours: 48);
        var tomatoes = await _client.CreateProductAsync(defaultShelfLifeHours: 120);
        var createdAt = Now.AddHours(-2);
        var salmonExpiry = createdAt.AddDays(3);

        var supply = await _client.CreateSupplyAsync(new
        {
            number = TestApi.Unique("SUP"),
            createdAt,
            supplier = "ООО Свежесть",
            items = new object[]
            {
                new { productId = cream, name = "Сливки 33%", quantity = 5000m, shelfLifeHours = 168 },
                new { productId = salmon, quantity = 3000m, expiresAt = salmonExpiry },
                new { productId = tomatoes, quantity = 4000m },
            },
        });

        var items = supply.GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(3, items.Count);
        Assert.Equal([1, 2, 3], items.Select(i => i.GetProperty("lineNo").GetInt32()));
        Assert.Equal(3, items.Select(i => i.GetProperty("batchId").GetGuid()).Distinct().Count());

        AssertExpiry(items[0], createdAt.AddHours(168), "SupplyItemShelfLife");
        AssertExpiry(items[1], salmonExpiry, "ExplicitDate");
        AssertExpiry(items[2], createdAt.AddHours(120), "ProductDefault");
        Assert.Equal("Сливки 33%", items[0].GetProperty("productName").GetString());

        var creamStock = await _client.GetStockItemAsync(cream);
        Assert.Equal(5000m, creamStock.GetProperty("usableQty").GetDecimal());
        Assert.Equal(1, creamStock.GetProperty("usableBatchCount").GetInt32());
        Assert.Equal("Fresh", creamStock.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Supply_is_readable_by_id_and_listed()
    {
        var product = await _client.CreateProductAsync();
        var number = TestApi.Unique("SUP");
        var created = await _client.CreateSupplyAsync(new { number, items = new[] { new { productId = product, quantity = 10m } } });
        var id = created.GetProperty("id").GetGuid();

        var byId = await _client.GetFromJsonAsync<JsonElement>($"/api/v1/supplies/{id}");
        var list = await _client.GetFromJsonAsync<JsonElement>("/api/v1/supplies?pageSize=100");

        Assert.Equal(number, byId.GetProperty("number").GetString());
        Assert.Equal(Now, byId.GetProperty("createdAt").GetDateTimeOffset());
        Assert.Contains(list.GetProperty("items").EnumerateArray(), s => s.GetProperty("id").GetGuid() == id);
    }

    [Fact]
    public async Task Kilograms_are_stored_as_grams()
    {
        var product = await _client.CreateProductAsync();

        var supply = await _client.CreateSupplyAsync(new
        {
            number = TestApi.Unique("SUP"),
            items = new[] { new { productId = product, quantity = 1.5m, unit = "Kilogram" } },
        });

        Assert.Equal(1500m, supply.GetProperty("items")[0].GetProperty("quantity").GetDecimal());
        Assert.Equal(1500m, (await _client.GetStockItemAsync(product)).GetProperty("usableQty").GetDecimal());
    }

    [Fact]
    public async Task Same_product_in_two_supplies_gives_two_batches_in_fefo_order()
    {
        var product = await _client.CreateProductAsync();
        await _client.CreateSupplyAsync(new
        {
            number = TestApi.Unique("SUP"),
            items = new[] { new { productId = product, quantity = 1000m, shelfLifeHours = 96 } },
        });
        await _client.CreateSupplyAsync(new
        {
            number = TestApi.Unique("SUP"),
            items = new[] { new { productId = product, quantity = 500m, shelfLifeHours = 24 } },
        });

        var result = await _client.GetFromJsonAsync<JsonElement>($"/api/v1/stock/{product}/batches");
        var batches = result.GetProperty("batches").EnumerateArray().ToList();

        Assert.Equal(2, batches.Count);
        Assert.Equal([500m, 1000m], batches.Select(b => b.GetProperty("remainingQty").GetDecimal()));
        Assert.Equal([1, 2], batches.Select(b => b.GetProperty("fefoRank").GetInt32()));
        Assert.Equal(1500m, result.GetProperty("stock").GetProperty("usableQty").GetDecimal());
        Assert.Equal(Now.AddHours(24), result.GetProperty("stock").GetProperty("nearestExpiresAt").GetDateTimeOffset());
    }

    [Fact]
    public async Task Expired_batch_leaves_usable_stock_but_stays_in_total()
    {
        var product = await _client.CreateProductAsync();
        await _client.CreateSupplyAsync(new
        {
            number = TestApi.Unique("SUP"),
            items = new object[]
            {
                new { productId = product, quantity = 300m, shelfLifeHours = 2 },
                new { productId = product, quantity = 700m, shelfLifeHours = 48 },
            },
        });

        var start = Now;
        try
        {
            factory.Clock.Advance(TimeSpan.FromMinutes(102)); // осталось 18 мин = 15 % → Critical
            var almost = await _client.GetStockItemAsync(product);
            Assert.Equal(1000m, almost.GetProperty("usableQty").GetDecimal());
            Assert.Equal("Critical", almost.GetProperty("status").GetString());

            factory.Clock.Advance(TimeSpan.FromMinutes(18)); // ровно момент истечения: now >= expires_at
            var stock = await _client.GetStockItemAsync(product);
            Assert.Equal(700m, stock.GetProperty("usableQty").GetDecimal());
            Assert.Equal(1000m, stock.GetProperty("totalQty").GetDecimal());
            Assert.Equal(300m, stock.GetProperty("expiredQty").GetDecimal());

            var batches = (await _client.GetFromJsonAsync<JsonElement>($"/api/v1/stock/{product}/batches"))
                .GetProperty("batches").EnumerateArray().ToList();
            var expired = batches.Single(b => b.GetProperty("remainingQty").GetDecimal() == 300m);
            Assert.False(expired.GetProperty("isUsable").GetBoolean());
            Assert.Equal("Expired", expired.GetProperty("shelfLife").GetProperty("status").GetString());
            Assert.Equal(JsonValueKind.Null, expired.GetProperty("fefoRank").ValueKind);
        }
        finally
        {
            factory.Clock.Set(start);
        }
    }

    [Fact]
    public async Task Backdated_supply_with_already_expired_item_is_registered_as_expired()
    {
        var product = await _client.CreateProductAsync();

        var supply = await _client.CreateSupplyAsync(new
        {
            number = TestApi.Unique("SUP"),
            createdAt = Now.AddDays(-5),
            items = new[] { new { productId = product, quantity = 100m, shelfLifeHours = 24 } },
        });

        Assert.Equal("Expired", supply.GetProperty("items")[0].GetProperty("batchStatus").GetString());
        var stock = await _client.GetStockItemAsync(product);
        Assert.Equal(0m, stock.GetProperty("usableQty").GetDecimal());
        Assert.Equal("OutOfStock", stock.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Repeated_request_with_same_idempotency_key_returns_original_supply()
    {
        var product = await _client.CreateProductAsync();
        var body = new { number = TestApi.Unique("SUP"), items = new[] { new { productId = product, quantity = 250m } } };
        var key = Guid.NewGuid().ToString();

        var first = await _client.PostSupplyAsync(body, key);
        var second = await _client.PostSupplyAsync(body, key);

        await first.EnsureStatusAsync(HttpStatusCode.Created);
        await second.EnsureStatusAsync(HttpStatusCode.OK);
        Assert.Equal((await first.ReadJsonAsync()).GetProperty("id").GetGuid(), (await second.ReadJsonAsync()).GetProperty("id").GetGuid());
        Assert.Equal(250m, (await _client.GetStockItemAsync(product)).GetProperty("usableQty").GetDecimal());
    }

    [Fact]
    public async Task Parallel_retries_with_same_key_create_exactly_one_supply()
    {
        var product = await _client.CreateProductAsync();
        var body = new { number = TestApi.Unique("SUP"), items = new[] { new { productId = product, quantity = 100m } } };
        var key = Guid.NewGuid().ToString();

        var responses = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => factory.CreateClient().PostSupplyAsync(body, key)));

        Assert.Equal(1, responses.Count(r => r.StatusCode == HttpStatusCode.Created));
        Assert.All(responses, r => Assert.Contains(r.StatusCode, new[] { HttpStatusCode.Created, HttpStatusCode.OK }));
        Assert.Single((await Task.WhenAll(responses.Select(r => r.ReadJsonAsync()))).Select(j => j.GetProperty("id").GetGuid()).Distinct());
        Assert.Equal(100m, (await _client.GetStockItemAsync(product)).GetProperty("usableQty").GetDecimal());

        await using var scope = factory.Services.CreateAsyncScope();

        using var demo = factory.AsDemoRestaurant();
        var db = scope.ServiceProvider.GetRequiredService<MiseDbContext>();
        Assert.Equal(1, await db.StockMovements.CountAsync(m => m.ProductId == product));
    }

    [Fact]
    public async Task Same_key_with_different_body_is_a_conflict()
    {
        var product = await _client.CreateProductAsync();
        var key = Guid.NewGuid().ToString();
        await _client.CreateSupplyAsync(new { number = TestApi.Unique("SUP"), items = new[] { new { productId = product, quantity = 1m } } }, key);

        var response = await _client.PostSupplyAsync(
            new { number = TestApi.Unique("SUP"), items = new[] { new { productId = product, quantity = 2m } } }, key);

        await AssertProblemAsync(response, HttpStatusCode.Conflict, "supply.idempotency_key_reused");
    }

    [Fact]
    public async Task Duplicate_number_without_key_is_a_conflict()
    {
        var product = await _client.CreateProductAsync();
        var number = TestApi.Unique("SUP");
        await _client.CreateSupplyAsync(new { number, items = new[] { new { productId = product, quantity = 1m } } });

        var response = await _client.PostSupplyAsync(new { number, items = new[] { new { productId = product, quantity = 1m } } });

        await AssertProblemAsync(response, HttpStatusCode.Conflict, "supply.duplicate_number");
    }

    public static TheoryData<string, string> InvalidSupplies => new()
    {
        { "empty-items", "items" },
        { "unknown-product", "items[0].productId" },
        { "negative-quantity", "items[0].quantity" },
        { "expires-before-created", "items[0].expiresAt" },
        { "future-created-at", "createdAt" },
        { "unit-mismatch", "items[0].quantity" },
        { "no-expiry-source", "items[0].expiresAt" },
    };

    [Theory]
    [MemberData(nameof(InvalidSupplies))]
    public async Task Invalid_supply_is_rejected_with_field_errors_and_nothing_is_stored(string scenario, string errorField)
    {
        var product = await _client.CreateProductAsync(defaultShelfLifeHours: scenario == "no-expiry-source" ? null : 72);
        var number = TestApi.Unique("SUP");
        object item = scenario switch
        {
            "unknown-product" => new { productId = Guid.NewGuid(), quantity = 1m },
            "negative-quantity" => new { productId = product, quantity = -5m },
            "expires-before-created" => new { productId = product, quantity = 1m, expiresAt = Now.AddHours(-1) },
            "unit-mismatch" => new { productId = product, quantity = 1m, unit = "Liter" },
            _ => new { productId = product, quantity = 1m },
        };
        object body = scenario switch
        {
            "empty-items" => new { number, items = Array.Empty<object>() },
            "future-created-at" => new { number, createdAt = Now.AddHours(1), items = new[] { item } },
            _ => new { number, items = new[] { item } },
        };

        var response = await _client.PostSupplyAsync(body);

        await response.EnsureStatusAsync(HttpStatusCode.BadRequest);
        var problem = await response.ReadJsonAsync();
        Assert.True(problem.GetProperty("errors").TryGetProperty(errorField, out _), problem.ToString());
        Assert.Equal(0m, (await _client.GetStockItemAsync(product)).GetProperty("totalQty").GetDecimal());
    }

    [Fact]
    public async Task Batches_feed_filters_by_shelf_status_and_horizon()
    {
        var product = await _client.CreateProductAsync();
        await _client.CreateSupplyAsync(new
        {
            number = TestApi.Unique("SUP"),
            items = new object[]
            {
                new { productId = product, quantity = 1m, shelfLifeHours = 4 },
                new { productId = product, quantity = 2m, expiresAt = Now.AddHours(5) },
                new { productId = product, quantity = 3m, shelfLifeHours = 240 },
            },
            createdAt = Now.AddHours(-20),
        });

        var critical = await _client.GetFromJsonAsync<JsonElement>($"/api/v1/batches?productId={product}&status=Critical");
        var within = await _client.GetFromJsonAsync<JsonElement>($"/api/v1/batches?productId={product}&expiringWithinHours=12");

        // 1-я партия истекла ещё до регистрации (−20 ч + 4 ч), 2-я: 5 ч из 25 ч = 20% → Critical.
        Assert.Equal([2m], critical.EnumerateArray().Select(b => b.GetProperty("remainingQty").GetDecimal()));
        Assert.Equal([1m, 2m], within.EnumerateArray().Select(b => b.GetProperty("remainingQty").GetDecimal()));
    }

    private static void AssertExpiry(JsonElement item, DateTimeOffset expected, string source)
    {
        Assert.Equal(expected, item.GetProperty("expiresAt").GetDateTimeOffset());
        Assert.Equal(source, item.GetProperty("expirySource").GetString());
    }

    private static async Task AssertProblemAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        await response.EnsureStatusAsync(status);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(code, (await response.ReadJsonAsync()).GetProperty("code").GetString());
    }
}
