using System.Net;
using System.Net.Http.Json;
using Mise.Domain.Users;

namespace Mise.Api.IntegrationTests;

/// <summary>«Что если»: банкет сравнивается с остатком к его началу, нехватка — с докупкой, блюда после него — в стоп; склад не меняется.</summary>
[Collection(ApiCollection.Name)]
public sealed class EventPlanningTests(MiseApiFactory factory)
{
    private readonly HttpClient _manager = factory.CreateClient();

    private Task<HttpResponseMessage> PlanAsync(HttpClient client, Guid dish, int portions, DateTimeOffset at) =>
        client.PostAsJsonAsync("/api/v1/planning/event", new { items = new[] { new { dishId = dish, portions } }, at });

    [Fact]
    public async Task Banquet_shows_shortage_purchase_and_dishes_going_to_stop_without_touching_stock()
    {
        var product = await _manager.CreateProductAsync();
        await _manager.ReceiveAsync(product, 1000m);
        var dish = await _manager.CreateDishAsync((product, 100m));
        var at = factory.Clock.UtcNow.AddHours(3);

        var tooBig = await (await PlanAsync(_manager, dish, 12, at)).ReadJsonAsync();
        Assert.False(tooBig.GetProperty("feasible").GetBoolean());
        var line = tooBig.GetProperty("lines").EnumerateArray().Single();
        Assert.Equal(1200m, line.GetProperty("need").GetDecimal());
        Assert.Equal(200m, line.GetProperty("shortage").GetDecimal());
        Assert.Equal(200m, line.GetProperty("buyQty").GetDecimal());
        Assert.Equal(6000m, tooBig.GetProperty("revenue").GetDecimal());

        var exact = await (await PlanAsync(_manager, dish, 10, at)).ReadJsonAsync();
        Assert.True(exact.GetProperty("feasible").GetBoolean());
        Assert.Equal(dish, exact.GetProperty("stopsAfter").EnumerateArray().Single().GetProperty("dishId").GetGuid());

        Assert.Equal(1000m, await _manager.UsableAsync(product));
        Assert.Equal(HttpStatusCode.BadRequest, (await PlanAsync(_manager, dish, 1, factory.Clock.UtcNow.AddHours(-1))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await PlanAsync(factory.CreateClientAs(UserRole.Warehouse), dish, 1, at)).StatusCode);
    }
}
