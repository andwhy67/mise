using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Mise.Application.Common;
using Mise.Application.Supplies;

namespace Mise.Api.Endpoints;

internal static class SupplyEndpoints
{
    public const string IdempotencyHeader = "Idempotency-Key";
    private const int IdempotencyKeyMaxLength = 200;

    public static RouteGroupBuilder MapSupplyEndpoints(this RouteGroupBuilder api)
    {
        var supplies = api.MapGroup("/supplies").WithTags("Поставки");

        supplies.MapPost("/", CreateAsync)
            .WithSummary("Принять поставку")
            .WithDescription(
                "Каждая позиция становится отдельной партией со своим сроком годности: явная дата → срок позиции от даты " +
                "создания поставки → срок по умолчанию из словаря. Повтор с тем же Idempotency-Key и тем же телом " +
                "возвращает прежний результат (200); тот же ключ с другим телом или повтор номера — 409.");
        supplies.MapGet("/", (SupplyService service, CancellationToken ct, int page = 1, int pageSize = 20) =>
                service.ListAsync(new PageRequest(page, pageSize), ct))
            .WithSummary("Список поставок (новые сверху)");
        supplies.MapGet("/{id:guid}", (Guid id, SupplyService service, CancellationToken ct) => service.GetAsync(id, ct))
            .WithName("GetSupply").WithSummary("Поставка с позициями и партиями");

        return api;
    }

    private static async Task<Results<CreatedAtRoute<SupplyDto>, Ok<SupplyDto>, ProblemHttpResult>> CreateAsync(
        CreateSupplyRequest request,
        [FromHeader(Name = IdempotencyHeader)] string? idempotencyKey,
        SupplyService service,
        CancellationToken ct)
    {
        if (idempotencyKey is { Length: > IdempotencyKeyMaxLength })
            return TypedResults.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Некорректный запрос",
                detail: $"{IdempotencyHeader} длиннее {IdempotencyKeyMaxLength} символов.");

        var result = await service.CreateAsync(request, idempotencyKey, ct);
        return result.Created
            ? TypedResults.CreatedAtRoute(result.Supply, "GetSupply", new { id = result.Supply.Id })
            : TypedResults.Ok(result.Supply);
    }
}
