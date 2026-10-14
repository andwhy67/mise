using Mise.Api.Auth;
using Mise.Application.Stock;
using Mise.Domain.Expiry;

namespace Mise.Api.Endpoints;

internal static class StockEndpoints
{
    public static RouteGroupBuilder MapStockEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/stock", (StockQueryService service, CancellationToken ct) => service.GetStockAsync(ct))
            .WithTags("Склад")
            .WithSummary("Остатки по продуктам: доступно (без просроченного), всего, ближайший срок, статус");

        api.MapGet("/stock/{productId:guid}/batches",
                (Guid productId, StockQueryService service, CancellationToken ct, bool includeClosed = false) =>
                    service.GetProductBatchesAsync(productId, includeClosed, ct))
            .WithTags("Склад")
            .WithSummary("Партии продукта в порядке FEFO (самый ранний срок первым)");

        api.MapGet("/batches", (
                StockQueryService service,
                CancellationToken ct,
                Guid? productId = null,
                ShelfLifeStatus[]? status = null,
                decimal? expiringWithinHours = null,
                bool includeClosed = false,
                int limit = 200) =>
                service.GetBatchesAsync(new BatchQuery(productId, status, expiringWithinHours, includeClosed, limit), ct))
            .WithTags("Склад")
            .WithSummary("Лента партий по сроку: фильтры по статусу срока (?status=Warning&status=Critical) и горизонту");

        api.MapPost("/batches/{id:guid}/write-off",
                (Guid id, WriteOffRequest request, WriteOffService service, CancellationToken ct) => service.WriteOffAsync(id, request, ct))
            .WithTags("Склад")
            .WithSummary("Ручное списание партии (причина обязательна)")
            .RequireAuthorization(AuthPolicies.Warehouse)
            .Audited("batch.write_off", "Batch")
            .WithDescription(
                "Без quantity списывается весь остаток. Можно списать активную или просроченную партию; " +
                "израсходованную/списанную — 409, больше остатка — 422.");

        return api;
    }
}
