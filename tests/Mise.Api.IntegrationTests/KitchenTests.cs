using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Mise.Domain.Users;

namespace Mise.Api.IntegrationTests;

/// <summary>Экран кухни: заказы в работе с составом; выданный уходит; кладовщику экран не нужен.</summary>
[Collection(ApiCollection.Name)]
public sealed class KitchenTests(MiseApiFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();

    private async Task<JsonElement?> CardAsync(Guid orderId) =>
        (await _client.GetFromJsonAsync<JsonElement>("/api/v1/kitchen/orders")).EnumerateArray()
            .Select(o => (JsonElement?)o).FirstOrDefault(o => o!.Value.GetProperty("id").GetGuid() == orderId);

    [Fact]
    public async Task Active_orders_come_with_dishes_and_status_time_and_leave_when_served()
    {
        var product = await _client.CreateProductAsync();
        await _client.ReceiveAsync(product, 1000m);
        var dish = await _client.CreateDishAsync((product, 100m));
        var order = await _client.CreateOrderAsync((dish, 3));
        var id = order.GetProperty("id").GetGuid();

        var card = (await CardAsync(id))!.Value;
        Assert.Equal("Created", card.GetProperty("status").GetString());
        Assert.Equal(3, card.GetProperty("portions").GetInt32());
        Assert.Equal(3, card.GetProperty("items")[0].GetProperty("quantity").GetInt32());

        factory.Clock.Advance(TimeSpan.FromMinutes(4));
        await (await _client.PatchAsJsonAsync($"/api/v1/orders/{id}/status", new { status = "Cooking" })).EnsureStatusAsync(HttpStatusCode.OK);
        card = (await CardAsync(id))!.Value;
        Assert.Equal("Cooking", card.GetProperty("status").GetString());
        Assert.Equal(factory.Clock.UtcNow, card.GetProperty("statusSince").GetDateTimeOffset());
        Assert.True(card.GetProperty("createdAt").GetDateTimeOffset() < card.GetProperty("statusSince").GetDateTimeOffset());

        await (await _client.PatchAsJsonAsync($"/api/v1/orders/{id}/status", new { status = "Done" })).EnsureStatusAsync(HttpStatusCode.OK);
        await (await _client.PatchAsJsonAsync($"/api/v1/orders/{id}/status", new { status = "Served" })).EnsureStatusAsync(HttpStatusCode.OK);
        Assert.Null(await CardAsync(id));
    }

    [Fact]
    public async Task Kitchen_is_for_the_kitchen_and_the_manager()
    {
        Assert.Equal(HttpStatusCode.OK, (await factory.CreateClientAs(UserRole.Chef).GetAsync("/api/v1/kitchen/orders")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await factory.CreateClientAs(UserRole.Warehouse).GetAsync("/api/v1/kitchen/orders")).StatusCode);
    }

    [Fact]
    public async Task New_order_is_never_cut_off_by_the_card_limit()
    {
        var product = await _client.CreateProductAsync();
        await _client.ReceiveAsync(product, 100_000m);
        var dish = await _client.CreateDishAsync((product, 1m));
        // Больше лимита заказов в работе (зависшие с прошлых смен) — последний всё равно на экране.
        for (var i = 0; i < 101; i++)
            await _client.CreateOrderAsync((dish, 1));
        factory.Clock.Advance(TimeSpan.FromSeconds(1));
        var newest = await _client.CreateOrderAsync((dish, 1));

        var cards = (await _client.GetFromJsonAsync<JsonElement>("/api/v1/kitchen/orders")).EnumerateArray().ToList();

        Assert.Equal(100, cards.Count);
        Assert.Equal(newest.GetProperty("id").GetGuid(), cards[^1].GetProperty("id").GetGuid());
    }
}
