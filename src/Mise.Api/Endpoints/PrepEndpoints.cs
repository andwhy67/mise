using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Mise.Api.Auth;
using Mise.Application.Catalog;
using Mise.Application.Prep;

namespace Mise.Api.Endpoints;

internal static class PrepEndpoints
{
    public static RouteGroupBuilder MapPrepEndpoints(this RouteGroupBuilder api)
    {
        var preps = api.MapGroup("/preps").WithTags("Заготовки");

        preps.MapGet("/", (PrepService service, CancellationToken ct) => service.ListAsync(ct))
            .WithSummary("Заготовки и сколько их можно приготовить из пригодных остатков");

        preps.MapPost("/{id:guid}/preview", (Guid id, CookPrepRequest request, PrepService service, CancellationToken ct) =>
                service.PreviewAsync(id, request, ct))
            .WithSummary("Что уйдёт в заготовку по FEFO, её срок и себестоимость — без изменений на складе")
            .RequireAuthorization(AuthPolicies.Orders);

        preps.MapPost("/{id:guid}/cook", CookAsync)
            .WithSummary("Приготовить заготовку: продукты списываются по FEFO, на склад приходит партия заготовки")
            .WithDescription("Срок заготовки — min(сейчас + срок хранения, самый ранний срок взятых партий). Нехватка — 422 с missing[].")
            .RequireAuthorization(AuthPolicies.Orders)
            .Audited("prep.cook", "Product");

        api.MapPut("/products/{id:guid}/prep", (Guid id, SetPrepRecipeRequest request, ProductService service, CancellationToken ct) =>
                service.SetPrepRecipeAsync(id, request, ct))
            .WithTags("Справочники")
            .WithSummary("Рецепт заготовки: состав на выход; пустой состав — продукт снова закупается")
            .RequireAuthorization(AuthPolicies.Manager)
            .Audited("product.prep", "Product");

        return api;
    }

    private static async Task<Results<Ok<PrepResultDto>, ProblemHttpResult>> CookAsync(
        Guid id,
        CookPrepRequest request,
        [FromHeader(Name = SupplyEndpoints.IdempotencyHeader)] string? idempotencyKey,
        PrepService service,
        CancellationToken ct)
    {
        if (idempotencyKey is { Length: > SupplyEndpoints.IdempotencyKeyMaxLength })
            return TypedResults.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Некорректный запрос",
                detail: $"{SupplyEndpoints.IdempotencyHeader} длиннее {SupplyEndpoints.IdempotencyKeyMaxLength} символов.");
        return TypedResults.Ok(await service.CookAsync(id, request, idempotencyKey, ct));
    }
}
