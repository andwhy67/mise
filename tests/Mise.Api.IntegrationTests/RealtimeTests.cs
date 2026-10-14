using System.Net.Http.Json;
using System.Text.Json;

namespace Mise.Api.IntegrationTests;

/// <summary>SignalR-хаб /hubs/stock: после поставки, заказа, отмены и тика просрочки клиент получает дельты.</summary>
[Collection(ApiCollection.Name)]
public sealed class RealtimeTests(MiseApiFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task Supply_and_order_push_stock_availability_stop_list_and_alerts()
    {
        await using var hub = await HubRecorder.ConnectAsync(factory);
        var meat = await _client.CreateProductAsync();
        var salt = await _client.CreateProductAsync();
        var dish = await _client.CreateDishAsync((meat, 100m), (salt, 10m));
        var number = TestApi.Unique("SUP");
        var received = await _client.CreateSupplyAsync(new { number, items = new[] { new { productId = meat, quantity = 100m, shelfLifeHours = 72 } } });
        var meatBatch = received.GetProperty("items")[0].GetProperty("batchId").GetGuid();
        await _client.ReceiveAsync(salt, 1000m);

        // Хаб общий для всех тестов коллекции: ждём именно свою поставку (события прошлых тестов могут ещё догонять).
        var supply = await hub.WaitForAsync("SupplyReceived", e => e.GetProperty("supply").GetProperty("number").GetString() == number);
        Assert.Equal(1, supply.GetProperty("supply").GetProperty("itemCount").GetInt32());
        await hub.WaitForAsync("StockChanged", e => e.Items("items").Any(i => i.Has("productId", meat) && i.GetProperty("usableQty").GetDecimal() == 100m));
        await hub.WaitForAsync("AvailabilityChanged", e => e.Items("dishes").Any(d => d.Has("dishId", dish) && d.GetProperty("maxPortions").GetInt32() == 1));

        var order = await _client.CreateOrderAsync((dish, 1));
        var orderId = order.GetProperty("id").GetGuid();

        var created = await hub.WaitForAsync("OrderCreated", e => e.GetProperty("order").Has("id", orderId));
        Assert.Equal("Created", created.GetProperty("order").GetProperty("status").GetString());
        // Один заказ по двум продуктам → одно событие StockChanged с обоими.
        await hub.WaitForAsync("StockChanged", e =>
            e.Items("items").Any(i => i.Has("productId", meat) && i.GetProperty("usableQty").GetDecimal() == 0m)
            && e.Items("items").Any(i => i.Has("productId", salt) && i.GetProperty("usableQty").GetDecimal() == 990m));
        await hub.WaitForAsync("BatchStatusChanged", e => e.Items("batches").Any(b => b.Has("id", meatBatch)
            && b.GetProperty("status").GetString() == "Depleted"));
        await hub.WaitForAsync("AvailabilityChanged", e => e.Items("dishes").Any(d => d.Has("dishId", dish)
            && d.GetProperty("maxPortions").GetInt32() == 0 && d.GetProperty("status").GetString() == "StopShortage"));
        var stop = await hub.WaitForAsync("StopListChanged", e => e.Items("added").Any(s => s.Has("dishId", dish)));
        var entry = stop.Items("added").Single(s => s.Has("dishId", dish));
        Assert.Equal(meat, entry.GetProperty("limitingProduct").GetProperty("productId").GetGuid());
        Assert.NotEqual(JsonValueKind.Null, entry.GetProperty("since").ValueKind);
        await hub.WaitForAsync("AlertRaised", e => e.Items("alerts").Any(a => a.Has("dishId", dish)
            && a.GetProperty("type").GetString() == "StopListOn"));

        // Отмена: заказ меняет статус, партия снова активна, блюдо выходит из стопа.
        await (await _client.PostAsync($"/api/v1/orders/{orderId}/cancel", null)).EnsureStatusAsync(System.Net.HttpStatusCode.OK);

        await hub.WaitForAsync("OrderStatusChanged", e => e.GetProperty("order").Has("id", orderId)
            && e.GetProperty("order").GetProperty("status").GetString() == "Cancelled");
        await hub.WaitForAsync("BatchStatusChanged", e => e.Items("batches").Any(b => b.Has("id", meatBatch)
            && b.GetProperty("status").GetString() == "Active"));
        await hub.WaitForAsync("StopListChanged", e => e.Items("removed").Any(r => r.GetGuid() == dish));
    }

    [Fact]
    public async Task Expiry_tick_pushes_written_off_batch_and_alert()
    {
        await using var hub = await HubRecorder.ConnectAsync(factory);
        var product = await _client.CreateProductAsync();
        var batch = await _client.ReceiveAsync(product, 50m, shelfLifeHours: 1);
        factory.Clock.Advance(TimeSpan.FromHours(1));

        await factory.RunExpiryTickAsync();

        await hub.WaitForAsync("BatchStatusChanged", e => e.Items("batches").Any(b => b.Has("id", batch)
            && b.GetProperty("status").GetString() == "WrittenOff"));
        await hub.WaitForAsync("StockChanged", e => e.Items("items").Any(i => i.Has("productId", product)
            && i.GetProperty("usableQty").GetDecimal() == 0m));
        var alert = await hub.WaitForAsync("AlertRaised", e => e.Items("alerts").Any(a => a.Has("batchId", batch)));
        Assert.Equal("Expired", alert.Items("alerts").Single(a => a.Has("batchId", batch)).GetProperty("type").GetString());
    }

    [Fact]
    public async Task Hub_is_reachable_and_rest_stays_source_of_truth()
    {
        // Клиент после реконнекта делает refetch: REST и событие должны совпадать.
        await using var hub = await HubRecorder.ConnectAsync(factory);
        var product = await _client.CreateProductAsync();
        await _client.ReceiveAsync(product, 321m);

        var pushed = await hub.WaitForAsync("StockChanged", e => e.Items("items").Any(i => i.Has("productId", product)));
        var rest = await _client.GetStockItemAsync(product);

        var item = pushed.Items("items").Single(i => i.Has("productId", product));
        Assert.Equal(rest.GetProperty("usableQty").GetDecimal(), item.GetProperty("usableQty").GetDecimal());
        Assert.Equal(rest.GetProperty("status").GetString(), item.GetProperty("status").GetString());
    }
}
