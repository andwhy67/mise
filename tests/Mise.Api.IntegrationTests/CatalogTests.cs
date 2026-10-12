using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Mise.Infrastructure.Seed;

namespace Mise.Api.IntegrationTests;

[Collection(ApiCollection.Name)]
public sealed class CatalogTests(MiseApiFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task Seed_provides_products_and_dishes_with_shared_ingredients()
    {
        var products = await _client.GetFromJsonAsync<JsonElement>("/api/v1/products");
        var dishes = await _client.GetFromJsonAsync<JsonElement>("/api/v1/dishes");

        Assert.True(products.GetArrayLength() >= 26);
        Assert.True(dishes.GetArrayLength() >= 17);
        var withCream = dishes.EnumerateArray().Count(d => d.GetProperty("ingredients").EnumerateArray()
            .Any(i => i.GetProperty("productId").GetGuid() == SeedData.Products.Cream));
        Assert.True(withCream >= 5, $"Сливки должны быть в нескольких блюдах, найдено {withCream}");
    }

    [Fact]
    public async Task Product_name_must_be_unique_case_insensitive()
    {
        var name = TestApi.Unique("Укроп");
        var first = await _client.PostAsJsonAsync("/api/v1/products", NewProduct(name));
        var second = await _client.PostAsJsonAsync("/api/v1/products", NewProduct(name.ToUpperInvariant()));

        await first.EnsureStatusAsync(HttpStatusCode.Created);
        await second.EnsureStatusAsync(HttpStatusCode.Conflict);
        Assert.Equal("product.duplicate_name", (await second.ReadJsonAsync()).GetProperty("code").GetString());
    }

    [Fact]
    public async Task Product_used_in_dish_cannot_be_deleted_or_change_unit()
    {
        var product = await _client.CreateProductAsync();
        await CreateDishAsync([(product, 10m)]);

        var delete = await _client.DeleteAsync($"/api/v1/products/{product}");
        var changeUnit = await _client.PutAsJsonAsync($"/api/v1/products/{product}", NewProduct(TestApi.Unique("Иное"), unit: "Piece"));

        await delete.EnsureStatusAsync(HttpStatusCode.Conflict);
        await changeUnit.EnsureStatusAsync(HttpStatusCode.Conflict);
        Assert.Equal("product.unit_locked", (await changeUnit.ReadJsonAsync()).GetProperty("code").GetString());
    }

    [Fact]
    public async Task Unused_product_can_be_deleted()
    {
        var product = await _client.CreateProductAsync();

        var delete = await _client.DeleteAsync($"/api/v1/products/{product}");
        var get = await _client.GetAsync($"/api/v1/products/{product}");

        await delete.EnsureStatusAsync(HttpStatusCode.NoContent);
        await get.EnsureStatusAsync(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Dish_ingredients_can_be_replaced()
    {
        var a = await _client.CreateProductAsync();
        var b = await _client.CreateProductAsync();
        var c = await _client.CreateProductAsync();
        var dishId = await CreateDishAsync([(a, 100m), (b, 50m)]);

        var response = await _client.PutAsJsonAsync($"/api/v1/dishes/{dishId}/ingredients", new
        {
            ingredients = new[] { new { productId = b, quantity = 70m }, new { productId = c, quantity = 5m } },
        });

        await response.EnsureStatusAsync(HttpStatusCode.OK);
        var ingredients = (await _client.GetFromJsonAsync<JsonElement>($"/api/v1/dishes/{dishId}"))
            .GetProperty("ingredients").EnumerateArray()
            .ToDictionary(i => i.GetProperty("productId").GetGuid(), i => i.GetProperty("quantity").GetDecimal());
        Assert.Equal(new Dictionary<Guid, decimal> { [b] = 70m, [c] = 5m }, ingredients);
    }

    [Fact]
    public async Task Dish_with_unknown_or_duplicate_ingredient_is_rejected()
    {
        var product = await _client.CreateProductAsync();

        var unknown = await _client.PostAsJsonAsync("/api/v1/dishes", NewDish([(Guid.NewGuid(), 10m)]));
        var duplicate = await _client.PostAsJsonAsync("/api/v1/dishes", NewDish([(product, 10m), (product, 5m)]));

        await unknown.EnsureStatusAsync(HttpStatusCode.BadRequest);
        Assert.True((await unknown.ReadJsonAsync()).GetProperty("errors").TryGetProperty("ingredients[0].productId", out _));
        await duplicate.EnsureStatusAsync(HttpStatusCode.BadRequest);
        Assert.True((await duplicate.ReadJsonAsync()).GetProperty("errors").TryGetProperty("ingredients", out _));
    }

    [Fact]
    public async Task Validation_messages_are_in_russian()
    {
        var response = await _client.PostAsJsonAsync("/api/v1/products", NewProduct(name: ""));

        await response.EnsureStatusAsync(HttpStatusCode.BadRequest);
        var message = (await response.ReadJsonAsync()).GetProperty("errors").GetProperty("name")[0].GetString();
        Assert.Matches("[А-Яа-я]", message);
    }

    private async Task<Guid> CreateDishAsync((Guid ProductId, decimal Quantity)[] ingredients)
    {
        var response = await _client.PostAsJsonAsync("/api/v1/dishes", NewDish(ingredients));
        await response.EnsureStatusAsync(HttpStatusCode.Created);
        return (await response.ReadJsonAsync()).GetProperty("id").GetGuid();
    }

    private static object NewProduct(string name, string unit = "Gram") =>
        new { name, unit, defaultShelfLifeHours = 72, minStockQty = 0m, packSize = 1m };

    private static object NewDish((Guid ProductId, decimal Quantity)[] ingredients) => new
    {
        name = TestApi.Unique("Блюдо"),
        category = "Тест",
        price = 100m,
        isActive = true,
        ingredients = ingredients.Select(i => new { productId = i.ProductId, quantity = i.Quantity }),
    };
}
