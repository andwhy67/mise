using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Mise.Api.IntegrationTests;

/// <summary>Деньги: цена закупки в поставке → стоимость каждого движения → себестоимость, маржа, потери, «спасено».</summary>
[Collection(ApiCollection.Name)]
public sealed class CostingTests(MiseApiFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();

    private async Task<Guid> ProductAsync(decimal? referencePrice)
    {
        var response = await _client.PostAsJsonAsync("/api/v1/products", new
        {
            name = TestApi.Unique("Цена"),
            unit = "Gram",
            defaultShelfLifeHours = 72,
            minStockQty = 0m,
            packSize = 1000m,
            category = "Тест",
            referencePrice,
            supplier = "Рыбный двор",
        });
        await response.EnsureStatusAsync(HttpStatusCode.Created);
        return (await response.ReadJsonAsync()).GetProperty("id").GetGuid();
    }

    /// <summary>Поставка с ценой за кг; возвращает строку поставки.</summary>
    private async Task<JsonElement> ReceiveAsync(Guid product, decimal kg, decimal? pricePerKg, int shelfLifeHours = 200, int receivedHoursAgo = 0)
    {
        var supply = await _client.CreateSupplyAsync(new
        {
            number = TestApi.Unique("SUP"),
            createdAt = factory.Clock.UtcNow.AddHours(-receivedHoursAgo),
            items = new[] { new { productId = product, quantity = kg, unit = "Kilogram", shelfLifeHours, price = pricePerKg } },
        });
        return supply.GetProperty("items")[0];
    }

    private async Task<JsonElement> MoneyAsync(DateTimeOffset from) =>
        await _client.GetFromJsonAsync<JsonElement>(
            $"/api/v1/analytics/money?from={Uri.EscapeDataString(from.ToString("O"))}&to={Uri.EscapeDataString(factory.Clock.UtcNow.AddMinutes(1).ToString("O"))}");

    [Fact]
    public async Task Product_keeps_reference_price_per_kg_and_supplier()
    {
        var product = await ProductAsync(referencePrice: 1900m);

        var card = await _client.GetFromJsonAsync<JsonElement>($"/api/v1/products/{product}");

        Assert.Equal(1900m, card.GetProperty("referencePrice").GetDecimal());
        Assert.Equal("Рыбный двор", card.GetProperty("supplier").GetString());
    }

    [Fact]
    public async Task Supply_line_price_is_per_entered_unit_and_falls_back_to_reference_price()
    {
        var product = await ProductAsync(referencePrice: 1000m);

        var priced = await ReceiveAsync(product, 2m, pricePerKg: 900m);
        var unpriced = await ReceiveAsync(product, 1m, pricePerKg: null);

        Assert.Equal(900m, priced.GetProperty("price").GetDecimal());
        Assert.Equal(1800m, priced.GetProperty("lineCost").GetDecimal());
        Assert.Equal(1000m, unpriced.GetProperty("price").GetDecimal());
    }

    [Fact]
    public async Task Money_report_counts_purchases_cost_of_sales_margin_losses_and_rescued()
    {
        var from = factory.Clock.UtcNow;
        var product = await ProductAsync(referencePrice: null);
        // Старая партия (привезли 60 ч назад со сроком 72 ч — осталось 12 ч, 17 % срока; 900 ₽/кг) уйдёт в заказ первой — она «спасается».
        await ReceiveAsync(product, 1m, pricePerKg: 900m, shelfLifeHours: 72, receivedHoursAgo: 60);
        await ReceiveAsync(product, 1m, pricePerKg: 1000m, shelfLifeHours: 200);
        var dish = await _client.CreateDishAsync((product, 100m)); // цена блюда 500 ₽

        await _client.CreateOrderAsync((dish, 3)); // 300 г × 0,9 ₽ = 270 ₽, выручка 1500 ₽
        var batches = await _client.GetFromJsonAsync<JsonElement>($"/api/v1/stock/{product}/batches");
        var fresh = batches.GetProperty("batches").EnumerateArray().Single(b => b.GetProperty("initialQty").GetDecimal() == 1000m
            && b.GetProperty("remainingQty").GetDecimal() == 1000m && b.GetProperty("shelfLife").GetProperty("hoursLeft").GetDecimal() > 100m);
        await (await _client.PostAsJsonAsync($"/api/v1/batches/{fresh.GetProperty("id").GetGuid()}/write-off", new { quantity = 50m, reason = "Брак" }))
            .EnsureStatusAsync(HttpStatusCode.OK); // 50 г × 1 ₽ = 50 ₽

        var money = await MoneyAsync(from);

        Assert.True(money.GetProperty("purchases").GetDecimal() >= 1900m);
        var product1 = money.GetProperty("products").EnumerateArray().Single(p => p.GetProperty("productId").GetGuid() == product);
        Assert.Equal(1900m, product1.GetProperty("purchased").GetDecimal());
        Assert.Equal(270m, product1.GetProperty("costOfSales").GetDecimal());
        Assert.Equal(50m, product1.GetProperty("losses").GetDecimal());
        Assert.Equal(270m, product1.GetProperty("rescued").GetDecimal());
        Assert.Equal(300m, product1.GetProperty("rescuedQty").GetDecimal());
    }

    [Fact]
    public async Task Cancelled_order_returns_its_cost_and_its_rescued_amount()
    {
        var from = factory.Clock.UtcNow;
        var product = await ProductAsync(referencePrice: null);
        await ReceiveAsync(product, 1m, pricePerKg: 900m, shelfLifeHours: 72, receivedHoursAgo: 60);
        var dish = await _client.CreateDishAsync((product, 100m));

        var order = await _client.CreateOrderAsync((dish, 2));
        await (await _client.PostAsync($"/api/v1/orders/{order.GetProperty("id").GetGuid()}/cancel", null)).EnsureStatusAsync(HttpStatusCode.OK);

        var money = await MoneyAsync(from);
        var item = money.GetProperty("products").EnumerateArray().Single(p => p.GetProperty("productId").GetGuid() == product);
        Assert.Equal(0m, item.GetProperty("costOfSales").GetDecimal());
        Assert.Equal(0m, item.GetProperty("rescued").GetDecimal());
    }

    [Fact]
    public async Task Dish_economics_by_the_first_batches_in_fefo_order()
    {
        var product = await ProductAsync(referencePrice: 2000m);
        await ReceiveAsync(product, 1m, pricePerKg: 900m, shelfLifeHours: 48);
        await ReceiveAsync(product, 1m, pricePerKg: 1200m, shelfLifeHours: 200);
        var dish = await _client.CreateDishAsync((product, 100m));

        var economics = await _client.GetFromJsonAsync<JsonElement>("/api/v1/analytics/dish-economics");

        var item = economics.EnumerateArray().Single(d => d.GetProperty("dishId").GetGuid() == dish);
        Assert.Equal(90m, item.GetProperty("portionCost").GetDecimal());
        Assert.Equal(410m, item.GetProperty("margin").GetDecimal());
        Assert.Equal(0.18m, item.GetProperty("foodCostShare").GetDecimal());
        Assert.False(item.GetProperty("estimated").GetBoolean());
    }
}
