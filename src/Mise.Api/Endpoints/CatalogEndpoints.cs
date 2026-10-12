using Mise.Application.Catalog;

namespace Mise.Api.Endpoints;

internal static class CatalogEndpoints
{
    public static RouteGroupBuilder MapCatalogEndpoints(this RouteGroupBuilder api)
    {
        var products = api.MapGroup("/products").WithTags("Справочник: продукты");

        products.MapGet("/", (ProductService service, CancellationToken ct) => service.ListAsync(ct))
            .WithSummary("Список продуктов");
        products.MapGet("/{id:guid}", (Guid id, ProductService service, CancellationToken ct) => service.GetAsync(id, ct))
            .WithName("GetProduct").WithSummary("Продукт по id");
        products.MapPost("/", async (UpsertProductRequest request, ProductService service, CancellationToken ct) =>
            {
                var product = await service.CreateAsync(request, ct);
                return TypedResults.CreatedAtRoute(product, "GetProduct", new { id = product.Id });
            })
            .WithSummary("Добавить продукт в словарь");
        products.MapPut("/{id:guid}", (Guid id, UpsertProductRequest request, ProductService service, CancellationToken ct) =>
                service.UpdateAsync(id, request, ct))
            .WithSummary("Изменить продукт");
        products.MapDelete("/{id:guid}", async (Guid id, ProductService service, CancellationToken ct) =>
            {
                await service.DeleteAsync(id, ct);
                return TypedResults.NoContent();
            })
            .WithSummary("Удалить продукт (409, если он используется)");

        var dishes = api.MapGroup("/dishes").WithTags("Справочник: блюда");

        dishes.MapGet("/", (DishService service, CancellationToken ct) => service.ListAsync(ct))
            .WithSummary("Список блюд с составом");
        dishes.MapGet("/{id:guid}", (Guid id, DishService service, CancellationToken ct) => service.GetAsync(id, ct))
            .WithName("GetDish").WithSummary("Блюдо по id");
        dishes.MapPost("/", async (CreateDishRequest request, DishService service, CancellationToken ct) =>
            {
                var dish = await service.CreateAsync(request, ct);
                return TypedResults.CreatedAtRoute(dish, "GetDish", new { id = dish.Id });
            })
            .WithSummary("Добавить блюдо с составом");
        dishes.MapPut("/{id:guid}", (Guid id, UpdateDishRequest request, DishService service, CancellationToken ct) =>
                service.UpdateAsync(id, request, ct))
            .WithSummary("Изменить блюдо (название, категория, цена, ручное включение/выключение)");
        dishes.MapPut("/{id:guid}/ingredients", (Guid id, SetDishIngredientsRequest request, DishService service, CancellationToken ct) =>
                service.SetIngredientsAsync(id, request, ct))
            .WithSummary("Заменить состав блюда");
        dishes.MapDelete("/{id:guid}", async (Guid id, DishService service, CancellationToken ct) =>
            {
                await service.DeleteAsync(id, ct);
                return TypedResults.NoContent();
            })
            .WithSummary("Удалить блюдо");

        return api;
    }
}
