using Mise.Domain.Alerts;
using Mise.Domain.Common;

namespace Mise.Application.Alerts;

/// <param name="IsActive">Причина ещё актуальна (для ExpiringSoon/LowStock); события всегда активны.</param>
public sealed record AlertDto(
    Guid Id,
    AlertType Type,
    AlertSeverity Severity,
    string Message,
    Guid? BatchId,
    Guid? ProductId,
    string? ProductName,
    Guid? DishId,
    string? DishName,
    DateTimeOffset CreatedAt,
    DateTimeOffset? AcknowledgedAt,
    DateTimeOffset? ResolvedAt,
    bool IsActive,
    MeasureUnit? Unit,
    decimal? Quantity,
    decimal? LimitQty,
    DateTimeOffset? ExpiresAt,
    bool? AutoWrittenOff);

/// <summary>Фильтры ленты алертов; все необязательные.</summary>
public sealed record AlertQuery(
    IReadOnlyCollection<AlertType>? Types = null,
    IReadOnlyCollection<AlertSeverity>? Severities = null,
    bool? Acknowledged = null,
    bool? Active = null,
    Guid? ProductId = null,
    Guid? BatchId = null,
    Guid? DishId = null,
    DateTimeOffset? Since = null);

/// <summary>Сколько алертов требуют внимания: не подтверждены и всё ещё актуальны.</summary>
public sealed record AlertSummaryDto(int Total, int Critical, int Warning, int Info);
