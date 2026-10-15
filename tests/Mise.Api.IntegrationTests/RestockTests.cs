using System.Net.Http.Json;
using System.Text.Json;

namespace Mise.Api.IntegrationTests;

/// <summary>«Что докупить, чтобы вернуть блюда из стопа».</summary>
[Collection(ApiCollection.Name)]
public sealed class RestockTests(MiseApiFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task Stopped_dishes_get_a_shared_purchase_with_the_dishes_it_returns()
    {
        var salmon = await _client.CreateProductAsync();
        var lemon = await _client.CreateProductAsync(unit: "Piece");
        await _client.ReceiveAsync(lemon, 100m);
        await _client.ReceiveAsync(salmon, 150m);
        var steak = await _client.CreateDishAsync((salmon, 200m), (lemon, 1m)); // 150 г < 200 — в стопе
        var tartare = await _client.CreateDishAsync((salmon, 100m));            // 150 г хватает — не в стопе

        var restock = await _client.GetFromJsonAsync<JsonElement>("/api/v1/stoplist/restock");

        var dish = restock.GetProperty("dishes").EnumerateArray().Single(d => d.GetProperty("dishId").GetGuid() == steak);
        Assert.Equal(5, dish.GetProperty("targetPortions").GetInt32());
        var line = Assert.Single(dish.GetProperty("lines").EnumerateArray());
        Assert.Equal(salmon, line.GetProperty("productId").GetGuid());
        Assert.Equal(850m, line.GetProperty("missing").GetDecimal());
        Assert.DoesNotContain(restock.GetProperty("dishes").EnumerateArray(), d => d.GetProperty("dishId").GetGuid() == tartare);

        var purchase = restock.GetProperty("purchases").EnumerateArray().Single(p => p.GetProperty("productId").GetGuid() == salmon);
        Assert.Equal(850m, purchase.GetProperty("toBuy").GetDecimal()); // упаковка 1 г
        Assert.Single(purchase.GetProperty("dishes").EnumerateArray());
        Assert.False(string.IsNullOrWhiteSpace(purchase.GetProperty("explanation").GetString()));
    }
}
