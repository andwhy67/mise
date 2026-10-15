using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Mise.Api.IntegrationTests;

/// <summary>Замены ингредиентов: правило в справочнике → 422 предлагает замену → повар соглашается → заказ из заменителя.</summary>
[Collection(ApiCollection.Name)]
public sealed class SubstitutionTests(MiseApiFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();

    private async Task SetSubstitutesAsync(Guid product, params (Guid SubstituteId, decimal Ratio)[] substitutes)
    {
        var card = await _client.GetFromJsonAsync<JsonElement>($"/api/v1/products/{product}");
        var response = await _client.PutAsJsonAsync($"/api/v1/products/{product}", new
        {
            name = card.GetProperty("name").GetString(),
            unit = card.GetProperty("unit").GetString(),
            defaultShelfLifeHours = 72,
            minStockQty = 0m,
            packSize = 1m,
            category = "Тест",
            substitutes = substitutes.Select(s => new { substituteId = s.SubstituteId, ratio = s.Ratio }),
        });
        await response.EnsureStatusAsync(HttpStatusCode.OK);
    }

    private Task<HttpResponseMessage> OrderAsync(Guid dish, int portions, params (Guid ProductId, Guid SubstituteId)[] substitutions) =>
        _client.PostAsJsonAsync("/api/v1/orders", new
        {
            items = new[] { new { dishId = dish, quantity = portions } },
            substitutions = substitutions.Select(s => new { productId = s.ProductId, substituteId = s.SubstituteId }),
        });

    [Fact]
    public async Task Product_card_lists_its_substitutes_and_null_keeps_them()
    {
        var spinach = await _client.CreateProductAsync();
        var arugula = await _client.CreateProductAsync();
        await SetSubstitutesAsync(spinach, (arugula, 1.2m));

        // Обновление без поля substitutes (старый клиент) правила не стирает.
        var card = await _client.GetFromJsonAsync<JsonElement>($"/api/v1/products/{spinach}");
        await (await _client.PutAsJsonAsync($"/api/v1/products/{spinach}", new
        {
            name = card.GetProperty("name").GetString(), unit = "Gram", defaultShelfLifeHours = 48, minStockQty = 0m, packSize = 1m,
        })).EnsureStatusAsync(HttpStatusCode.OK);

        card = await _client.GetFromJsonAsync<JsonElement>($"/api/v1/products/{spinach}");
        var substitute = Assert.Single(card.GetProperty("substitutes").EnumerateArray());
        Assert.Equal(arugula, substitute.GetProperty("productId").GetGuid());
        Assert.Equal(1.2m, substitute.GetProperty("ratio").GetDecimal());
        Assert.False(string.IsNullOrEmpty(substitute.GetProperty("productName").GetString()));
    }

    [Fact]
    public async Task Refusal_offers_a_substitute_and_the_order_with_consent_takes_it_from_the_substitute()
    {
        var cream = await _client.CreateProductAsync(unit: "Milliliter");
        var milk = await _client.CreateProductAsync(unit: "Milliliter");
        await _client.ReceiveAsync(cream, 50m);
        await _client.ReceiveAsync(milk, 1000m);
        await SetSubstitutesAsync(cream, (milk, 1.5m));
        var dish = await _client.CreateDishAsync((cream, 100m), (milk, 20m));

        var refused = await OrderAsync(dish, 2);
        await refused.EnsureStatusAsync(HttpStatusCode.UnprocessableEntity);
        var missing = (await refused.ReadJsonAsync()).GetProperty("missing").EnumerateArray().Single();
        var offer = Assert.Single(missing.GetProperty("substitutes").EnumerateArray());
        Assert.Equal(milk, offer.GetProperty("productId").GetGuid());
        Assert.Equal(1.5m, offer.GetProperty("ratio").GetDecimal());
        Assert.Equal(340m, offer.GetProperty("required").GetDecimal()); // 200 × 1,5 + своё молоко 40 — суммарно
        Assert.Equal(1000m, offer.GetProperty("available").GetDecimal());
        Assert.True(offer.GetProperty("enough").GetBoolean());

        var accepted = await OrderAsync(dish, 2, (cream, milk));
        await accepted.EnsureStatusAsync(HttpStatusCode.Created);
        var order = await accepted.ReadJsonAsync();
        var substitution = Assert.Single(order.GetProperty("substitutions").EnumerateArray());
        Assert.Equal(cream, substitution.GetProperty("productId").GetGuid());
        Assert.Equal(milk, substitution.GetProperty("substituteId").GetGuid());
        Assert.Equal(50m, await _client.UsableAsync(cream)); // сливки не тронуты
        Assert.Equal(660m, await _client.UsableAsync(milk));

        // Отмена возвращает в партии заменителя.
        await (await _client.PostAsync($"/api/v1/orders/{order.GetProperty("id").GetGuid()}/cancel", null)).EnsureStatusAsync(HttpStatusCode.OK);
        Assert.Equal(1000m, await _client.UsableAsync(milk));
    }

    [Fact]
    public async Task Substitute_that_is_also_short_is_still_refused_with_the_aggregate_check()
    {
        var cream = await _client.CreateProductAsync(unit: "Milliliter");
        var milk = await _client.CreateProductAsync(unit: "Milliliter");
        await _client.ReceiveAsync(milk, 100m);
        await SetSubstitutesAsync(cream, (milk, 1m));
        var dish = await _client.CreateDishAsync((cream, 100m));

        var refused = await OrderAsync(dish, 2, (cream, milk));

        await refused.EnsureStatusAsync(HttpStatusCode.UnprocessableEntity);
        var missing = (await refused.ReadJsonAsync()).GetProperty("missing").EnumerateArray().Single();
        Assert.Equal(milk, missing.GetProperty("productId").GetGuid());
        Assert.Equal(200m, missing.GetProperty("required").GetDecimal());
        Assert.Equal(100m, await _client.UsableAsync(milk));
    }

    [Fact]
    public async Task Substitution_without_a_rule_or_for_an_unused_product_is_a_bad_request()
    {
        var cream = await _client.CreateProductAsync(unit: "Milliliter");
        var milk = await _client.CreateProductAsync(unit: "Milliliter");
        var other = await _client.CreateProductAsync(unit: "Milliliter");
        await _client.ReceiveAsync(milk, 1000m);
        await SetSubstitutesAsync(other, (milk, 1m));
        var dish = await _client.CreateDishAsync((cream, 100m));

        await (await OrderAsync(dish, 1, (cream, milk))).EnsureStatusAsync(HttpStatusCode.BadRequest);  // правила нет
        await (await OrderAsync(dish, 1, (other, milk))).EnsureStatusAsync(HttpStatusCode.BadRequest);  // продукта нет в заказе
    }
}
