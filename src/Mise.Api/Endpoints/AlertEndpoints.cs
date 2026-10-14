using Mise.Api.Auth;
using Mise.Application.Alerts;
using Mise.Application.Common;
using Mise.Domain.Alerts;

namespace Mise.Api.Endpoints;

internal static class AlertEndpoints
{
    public static RouteGroupBuilder MapAlertEndpoints(this RouteGroupBuilder api)
    {
        var alerts = api.MapGroup("/alerts").WithTags("Алерты");

        alerts.MapGet("/", (
                AlertService service,
                CancellationToken ct,
                AlertType[]? type = null,
                AlertSeverity[]? severity = null,
                bool? acknowledged = null,
                bool? active = null,
                Guid? productId = null,
                Guid? batchId = null,
                Guid? dishId = null,
                DateTimeOffset? since = null,
                int page = 1,
                int pageSize = 50) =>
                service.ListAsync(
                    new AlertQuery(type, severity, acknowledged, active, productId, batchId, dishId, since),
                    new PageRequest(page, pageSize), ct))
            .WithSummary("Лента алертов (новые сверху)")
            .WithDescription(
                "Фильтры: ?type=ExpiringSoon&type=Expired, ?severity=Critical, ?acknowledged=false, " +
                "?active=true (причина ещё актуальна), productId/batchId/dishId, since.");

        alerts.MapGet("/summary", (AlertService service, CancellationToken ct) => service.GetSummaryAsync(ct))
            .WithSummary("Сколько алертов требуют внимания (не подтверждены и актуальны) — по важности");

        alerts.MapPost("/{id:guid}/ack", (Guid id, AlertService service, CancellationToken ct) => service.AcknowledgeAsync(id, ct))
            .WithSummary("Подтвердить алерт (идемпотентно)")
            .RequireAuthorization(AuthPolicies.Staff);

        return api;
    }
}
