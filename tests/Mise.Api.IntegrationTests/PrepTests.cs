using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Mise.Domain.Users;

namespace Mise.Api.IntegrationTests;

/// <summary>
/// Заготовки: продукты уходят по FEFO, на склад приходит партия заготовки со сроком не дольше взятых продуктов;
/// блюдо на заготовке продаётся обычным заказом; нехватка — 422 без списаний; повтор по Idempotency-Key — тот же результат.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class PrepTests(MiseApiFactory factory)
{
    private readonly HttpClient _manager = factory.CreateClient();

    /// <summary>Соус: на 1000 мл — 500 мл первого продукта и 400 г второго, хранится 24 ч.</summary>
    private async Task<(Guid Sauce, Guid Cream, Guid Mushrooms)> CreateSauceAsync()
    {
        var cream = await _manager.CreateProductAsync(unit: "Milliliter");
        var mushrooms = await _manager.CreateProductAsync();
        var sauce = await _manager.CreateProductAsync(unit: "Milliliter", defaultShelfLifeHours: 24);
        var recipe = await _manager.PutAsJsonAsync($"/api/v1/products/{sauce}/prep", new
        {
            yield = 1000m,
            ingredients = new[] { new { productId = cream, quantity = 500m }, new { productId = mushrooms, quantity = 400m } },
        });
        await recipe.EnsureStatusAsync(HttpStatusCode.OK);
        Assert.Equal(1000m, (await recipe.ReadJsonAsync()).GetProperty("prep").GetProperty("yield").GetDecimal());
        return (sauce, cream, mushrooms);
    }

    private async Task<HttpResponseMessage> CookAsync(HttpClient client, Guid sauce, decimal quantity, string? key = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/preps/{sauce}/cook") { Content = JsonContent.Create(new { quantity }) };
        if (key is not null)
            request.Headers.Add("Idempotency-Key", key);
        return await client.SendAsync(request);
    }

    [Fact]
    public async Task Cooking_takes_ingredients_by_fefo_and_the_prep_never_outlives_them()
    {
        var (sauce, cream, mushrooms) = await CreateSauceAsync();
        var earlyCream = await _manager.ReceiveAsync(cream, 300m, shelfLifeHours: 20);
        await _manager.ReceiveAsync(cream, 1000m, shelfLifeHours: 72);
        await _manager.ReceiveAsync(mushrooms, 1000m, shelfLifeHours: 96);

        var key = TestApi.Unique("prep");
        var response = await CookAsync(_manager, sauce, 1000m, key);
        await response.EnsureStatusAsync(HttpStatusCode.OK);
        var result = await response.ReadJsonAsync();
        Assert.StartsWith("ЗАГ-", result.GetProperty("number").GetString());

        // FEFO: сначала вся ранняя партия сливок (300), остальное (200) — из поздней.
        Assert.Equal(800m, await _manager.UsableAsync(cream));
        Assert.Equal(600m, await _manager.UsableAsync(mushrooms));
        Assert.Equal(1000m, await _manager.UsableAsync(sauce));

        var creamBatches = await _manager.GetFromJsonAsync<JsonElement>($"/api/v1/stock/{cream}/batches");
        Assert.DoesNotContain(creamBatches.GetProperty("batches").EnumerateArray(), b => b.GetProperty("id").GetGuid() == earlyCream);
        var earlyExpiry = (await _manager.GetFromJsonAsync<JsonElement>($"/api/v1/stock/{cream}/batches?includeClosed=true"))
            .GetProperty("batches").EnumerateArray().Single(b => b.GetProperty("id").GetGuid() == earlyCream).GetProperty("expiresAt").GetDateTimeOffset();

        // Соус хранится 24 ч, но сливки в нём — 20 ч: срок соуса — по сливкам.
        var sauceBatch = (await _manager.GetFromJsonAsync<JsonElement>($"/api/v1/stock/{sauce}/batches")).GetProperty("batches")[0];
        Assert.Equal("Prep", sauceBatch.GetProperty("expirySource").GetString());
        Assert.Equal(earlyExpiry, sauceBatch.GetProperty("expiresAt").GetDateTimeOffset());

        // Повтор с тем же ключом — тот же результат, повторного списания нет.
        var replay = await (await CookAsync(_manager, sauce, 1000m, key)).ReadJsonAsync();
        Assert.Equal(result.GetProperty("batchId").GetGuid(), replay.GetProperty("batchId").GetGuid());
        Assert.Equal(800m, await _manager.UsableAsync(cream));

        // Блюдо на заготовке (соус-добавка) продаётся обычным заказом.
        var side = await _manager.CreateDishAsync((sauce, 50m));
        await _manager.CreateOrderAsync((side, 2));
        Assert.Equal(900m, await _manager.UsableAsync(sauce));
    }

    [Fact]
    public async Task Shortage_is_refused_without_changes_and_only_kitchen_may_cook()
    {
        var (sauce, cream, mushrooms) = await CreateSauceAsync();
        await _manager.ReceiveAsync(cream, 1000m);
        await _manager.ReceiveAsync(mushrooms, 100m);

        var preview = await (await _manager.PostAsJsonAsync($"/api/v1/preps/{sauce}/preview", new { quantity = 1000m })).ReadJsonAsync();
        Assert.False(preview.GetProperty("canCook").GetBoolean());

        var refused = await CookAsync(_manager, sauce, 1000m);
        Assert.Equal((HttpStatusCode)422, refused.StatusCode);
        var missing = (await refused.ReadJsonAsync()).GetProperty("missing").EnumerateArray().Single();
        Assert.Equal(mushrooms, missing.GetProperty("productId").GetGuid());
        Assert.Equal(300m, missing.GetProperty("shortage").GetDecimal());
        Assert.Equal(1000m, await _manager.UsableAsync(cream));
        Assert.Equal(0m, await _manager.UsableAsync(sauce));

        Assert.Equal(HttpStatusCode.Conflict, (await CookAsync(_manager, cream, 100m)).StatusCode);
        await (await CookAsync(factory.CreateClientAs(UserRole.Chef), sauce, 200m)).EnsureStatusAsync(HttpStatusCode.OK);
        Assert.Equal(HttpStatusCode.Forbidden, (await CookAsync(factory.CreateClientAs(UserRole.Warehouse), sauce, 100m)).StatusCode);
    }
}
