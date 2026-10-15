using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Mise.Api.Auth;
using Mise.Application.Common;
using Mise.Application.Orders;
using Mise.Domain.Orders;

namespace Mise.Api.Endpoints;

internal static class OrderEndpoints
{
    public static RouteGroupBuilder MapOrderEndpoints(this RouteGroupBuilder api)
    {
        var orders = api.MapGroup("/orders").WithTags("Заказы");

        orders.MapPost("/", CreateAsync)
            .WithSummary("Создать заказ")
            .RequireAuthorization(AuthPolicies.Orders)
            .Audited("order.create", "Order")
            .WithDescription(
                "Наличие проверяется суммарно по всем блюдам заказа. При нехватке — 422 с missing[] " +
                "(продукт, нужно, доступно) и ничего не списывается. При успехе — списание по FEFO. " +
                "Повтор с тем же Idempotency-Key и тем же телом возвращает прежний заказ (200).")
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);
        api.MapGet("/kitchen/orders", (OrderService service, CancellationToken ct) => service.GetKitchenAsync(ct))
            .WithTags("Заказы")
            .RequireAuthorization(AuthPolicies.Dashboard)
            .WithSummary("Экран кухни: заказы в работе с составом и заменами, старые сверху")
            .WithDescription("statusSince — когда заказ перешёл в текущий статус (таймер колонки). Не больше 100 карточек.");

        orders.MapGet("/", (OrderService service, CancellationToken ct, OrderStatus? status = null, int page = 1, int pageSize = 20) =>
                service.ListAsync(status, new PageRequest(page, pageSize), ct))
            .WithSummary("Список заказов (новые сверху)");
        orders.MapGet("/{id:guid}", (Guid id, OrderService service, CancellationToken ct) => service.GetAsync(id, ct))
            .WithName("GetOrder").WithSummary("Заказ: позиции и из каких партий списано");
        orders.MapPost("/{id:guid}/cancel", (Guid id, OrderService service, CancellationToken ct) => service.CancelAsync(id, ct))
            .WithSummary("Отменить заказ: возврат в те же партии, если они ещё не просрочены")
            .RequireAuthorization(AuthPolicies.Orders)
            .Audited("order.cancel", "Order");
        orders.MapPatch("/{id:guid}/status", (Guid id, ChangeOrderStatusRequest request, OrderService service, CancellationToken ct) =>
                service.ChangeStatusAsync(id, request, ct))
            .WithSummary("Сменить статус: Created → Cooking → Done")
            .RequireAuthorization(AuthPolicies.Orders)
            .Audited("order.status", "Order");

        return api;
    }

    private static async Task<Results<CreatedAtRoute<OrderDto>, Ok<OrderDto>, ProblemHttpResult>> CreateAsync(
        CreateOrderRequest request,
        [FromHeader(Name = SupplyEndpoints.IdempotencyHeader)] string? idempotencyKey,
        OrderService service,
        CancellationToken ct)
    {
        if (idempotencyKey is { Length: > SupplyEndpoints.IdempotencyKeyMaxLength })
            return TypedResults.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Некорректный запрос",
                detail: $"{SupplyEndpoints.IdempotencyHeader} длиннее {SupplyEndpoints.IdempotencyKeyMaxLength} символов.");

        var result = await service.CreateAsync(request, idempotencyKey, ct);
        return result.Created
            ? TypedResults.CreatedAtRoute(result.Order, "GetOrder", new { id = result.Order.Id })
            : TypedResults.Ok(result.Order);
    }
}
