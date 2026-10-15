using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Mise.Api.IntegrationTests;

/// <summary>Лимоны кончились — лосось в стопе, но его можно подать «без лимона»: меню это подсказывает, заказ проходит.</summary>
[Collection(ApiCollection.Name)]
public sealed class ModificationTests(MiseApiFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task Dish_without_a_removable_garnish_is_offered_without_it_and_can_be_ordered()
    {
        var salmon = await _client.CreateProductAsync();
        var lemon = await _client.CreateProductAsync(unit: "Piece");
        await _client.ReceiveAsync(salmon, 1000m);
        var created = await _client.PostAsJsonAsync("/api/v1/dishes", new
        {
            name = TestApi.Unique("Лосось"),
            category = "Тест",
            price = 900m,
            isActive = true,
            ingredients = new object[]
            {
                new { productId = salmon, quantity = 200m },
                new { productId = lemon, quantity = 1m, removable = true },
            },
        });
        await created.EnsureStatusAsync(HttpStatusCode.Created);
        var dish = (await created.ReadJsonAsync()).GetProperty("id").GetGuid();

        // В стопе (лимонов нет), но с изменениями — 5 порций без лимона.
        var menu = await _client.GetFromJsonAsync<JsonElement>("/api/v1/menu/availability");
        var card = menu.EnumerateArray().Single(d => d.GetProperty("dishId").GetGuid() == dish);
        Assert.True(card.GetProperty("inStopList").GetBoolean());
        var alternative = card.GetProperty("alternative");
        Assert.Equal(5, alternative.GetProperty("portions").GetInt32());
        var change = Assert.Single(alternative.GetProperty("changes").EnumerateArray());
        Assert.Equal("Remove", change.GetProperty("kind").GetString());
        Assert.Equal(lemon, change.GetProperty("productId").GetGuid());

        // Без изменений — отказ; «без лимона» — принят, лимон не списан, кухня видит изменение.
        var refused = await _client.PostOrderAsync(null, (dish, 1));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, refused.StatusCode);
        var accepted = await _client.PostAsJsonAsync("/api/v1/orders", new
        {
            items = new[] { new { dishId = dish, quantity = 2, modifications = new[] { new { productId = lemon, kind = "Remove" } } } },
        });
        await accepted.EnsureStatusAsync(HttpStatusCode.Created);
        var order = await accepted.ReadJsonAsync();
        Assert.Equal("Remove", order.GetProperty("items")[0].GetProperty("modifications")[0].GetProperty("kind").GetString());
        Assert.Equal(600m, await _client.UsableAsync(salmon));

        var kitchen = await _client.GetFromJsonAsync<JsonElement>("/api/v1/kitchen/orders");
        var kitchenCard = kitchen.EnumerateArray().Single(o => o.GetProperty("id").GetGuid() == order.GetProperty("id").GetGuid());
        Assert.False(string.IsNullOrEmpty(kitchenCard.GetProperty("items")[0].GetProperty("modifications")[0].GetProperty("productName").GetString()));

        // Убрать можно только «можно убрать».
        var notAllowed = await _client.PostAsJsonAsync("/api/v1/orders", new
        {
            items = new[] { new { dishId = dish, quantity = 1, modifications = new[] { new { productId = salmon, kind = "Remove" } } } },
        });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, notAllowed.StatusCode);
    }
}
