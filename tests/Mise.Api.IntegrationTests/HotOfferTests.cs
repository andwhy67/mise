using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Mise.Application.Realtime;

namespace Mise.Api.IntegrationTests;

/// <summary>GET /hot-offers и HotOffersChanged на собственных продуктах теста (БД общая для коллекции).</summary>
[Collection(ApiCollection.Name)]
public sealed class HotOfferTests(MiseApiFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task Burning_ingredient_puts_its_dish_into_offers_with_explanation()
    {
        var spinach = await _client.CreateProductAsync(defaultShelfLifeHours: 72, minStockQty: 500);
        var pasta = await _client.CreateProductAsync(defaultShelfLifeHours: 8760);
        // Шпинат пришёл 64 ч назад со сроком 72 ч: осталось 8 ч из горизонта 36 ч.
        await _client.ReceiveAsync(spinach, 600m, shelfLifeHours: 72, createdAt: factory.Clock.UtcNow.AddHours(-64));
        await _client.ReceiveAsync(pasta, 5000m, shelfLifeHours: 8760);
        var hot = await _client.CreateDishAsync((spinach, 80m), (pasta, 100m));
        var plain = await _client.CreateDishAsync((pasta, 100m));

        var offers = await OffersAsync();

        var offer = offers.Single(o => o.GetProperty("dishId").GetGuid() == hot);
        Assert.DoesNotContain(offers, o => o.GetProperty("dishId").GetGuid() == plain);
        // u = (1 − 8/36)² ≈ 0,605; V = 1; доля = 1 → ≈ 60,5.
        Assert.InRange(offer.GetProperty("score").GetDecimal(), 60m, 61m);
        Assert.Equal(7, offer.GetProperty("maxPortions").GetInt32());
        var reason = offer.GetProperty("reasons").EnumerateArray().Single();
        Assert.Equal(spinach, reason.GetProperty("productId").GetGuid());
        Assert.Equal(600m, reason.GetProperty("atRiskQty").GetDecimal());
        Assert.Equal(8m, reason.GetProperty("hoursLeft").GetDecimal());
        Assert.Equal(7, reason.GetProperty("portionsBurned").GetInt32());
        Assert.Contains("600 г истекает через 8 ч; в порции 80 г → блюдо поможет списать до 7 порций сырья.",
            offer.GetProperty("explanation").GetString());
        Assert.StartsWith("Горит: ", offer.GetProperty("headline").GetString());
        Assert.True(offer.GetProperty("rank").GetInt32() >= 1);
    }

    [Fact]
    public async Task Dish_switched_off_or_out_of_stock_is_not_offered()
    {
        var cream = await _client.CreateProductAsync(defaultShelfLifeHours: 168);
        var shrimp = await _client.CreateProductAsync();
        await _client.ReceiveAsync(cream, 2000m, shelfLifeHours: 168, createdAt: factory.Clock.UtcNow.AddHours(-160));
        var outOfStock = await _client.CreateDishAsync((cream, 100m), (shrimp, 100m));
        var switchedOff = await _client.CreateDishAsync((cream, 100m));
        var dish = await _client.GetFromJsonAsync<JsonElement>($"/api/v1/dishes/{switchedOff}");
        var update = await _client.PutAsJsonAsync($"/api/v1/dishes/{switchedOff}", new
        {
            name = dish.GetProperty("name").GetString(),
            category = "Тест",
            price = 500m,
            isActive = false,
        });
        await update.EnsureStatusAsync(HttpStatusCode.OK);

        var offers = await OffersAsync();

        Assert.DoesNotContain(offers, o => o.GetProperty("dishId").GetGuid() == outOfStock);
        Assert.DoesNotContain(offers, o => o.GetProperty("dishId").GetGuid() == switchedOff);
    }

    [Fact]
    public async Task Both_scorer_versions_are_available_and_bad_parameters_are_rejected()
    {
        var v1 = await _client.GetFromJsonAsync<JsonElement>("/api/v1/hot-offers?scorer=v1&limit=3");
        Assert.Equal("v1", v1.GetProperty("scorer").GetString());
        Assert.True(v1.GetProperty("offers").GetArrayLength() <= 3);
        var byDefault = await _client.GetFromJsonAsync<JsonElement>("/api/v1/hot-offers");
        Assert.Equal("v2", byDefault.GetProperty("scorer").GetString());

        var unknown = await _client.GetAsync("/api/v1/hot-offers?scorer=v9");
        await unknown.EnsureStatusAsync(HttpStatusCode.BadRequest);
        Assert.True((await unknown.ReadJsonAsync()).GetProperty("errors").TryGetProperty("scorer", out _));
        var badLimit = await _client.GetAsync("/api/v1/hot-offers?limit=0");
        await badLimit.EnsureStatusAsync(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Offers_are_pushed_when_stock_changes_and_when_time_passes()
    {
        await using var hub = await HubRecorder.ConnectAsync(factory);
        var basil = await _client.CreateProductAsync(defaultShelfLifeHours: 72, minStockQty: 100);
        var dish = await _client.CreateDishAsync((basil, 10m));

        // Поставка уже «подсохшего» базилика: осталось 20 ч из горизонта 36 ч.
        await _client.ReceiveAsync(basil, 300m, shelfLifeHours: 72, createdAt: factory.Clock.UtcNow.AddHours(-52));
        var first = await hub.WaitForAsync("HotOffersChanged", e => ScoreOf(e, dish) is not null);
        var before = ScoreOf(first, dish)!.Value;

        // Событий нет, но прошло время — таймерный пересчёт поднимает срочность.
        factory.Clock.Advance(TimeSpan.FromHours(10));
        factory.Services.GetRequiredService<IRealtimeNotifier>().HotOffersMayHaveChanged();
        var later = await hub.WaitForAsync("HotOffersChanged", e => ScoreOf(e, dish) > before);

        Assert.Equal("v2", later.GetProperty("scorer").GetString());
    }

    private async Task<List<JsonElement>> OffersAsync() =>
        (await _client.GetFromJsonAsync<JsonElement>("/api/v1/hot-offers?limit=20"))
            .GetProperty("offers").EnumerateArray().ToList();

    private static decimal? ScoreOf(JsonElement hotOffers, Guid dishId) =>
        hotOffers.GetProperty("offers").EnumerateArray()
            .Where(o => o.GetProperty("dishId").GetGuid() == dishId)
            .Select(o => (decimal?)o.GetProperty("score").GetDecimal())
            .FirstOrDefault();
}
