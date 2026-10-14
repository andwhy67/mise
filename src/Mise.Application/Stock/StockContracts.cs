using Mise.Domain.Common;
using Mise.Domain.Expiry;
using Mise.Domain.Stock;

namespace Mise.Application.Stock;

/// <summary>Сводный статус продукта на складе.</summary>
public enum StockStatus
{
    Fresh,
    Warning,
    Critical,
    /// <summary>Нет пригодного остатка (возможно, есть просроченный, ещё не списанный).</summary>
    OutOfStock,
}

/// <param name="UsableQty">Доступный остаток: активные непросроченные партии.</param>
/// <param name="TotalQty">Физически на складе, включая просроченное, ещё не списанное.</param>
/// <param name="ExpiredQty">Просрочено, но ещё не списано.</param>
/// <param name="Status">Худший статус срока среди пригодных партий; OutOfStock, если пригодных нет.</param>
public sealed record StockItemDto(
    Guid ProductId,
    string ProductName,
    MeasureUnit Unit,
    string? Category,
    decimal UsableQty,
    decimal TotalQty,
    decimal ExpiredQty,
    decimal MinStockQty,
    bool BelowMinStock,
    int UsableBatchCount,
    DateTimeOffset? NearestExpiresAt,
    decimal? NearestHoursLeft,
    StockStatus Status);

public sealed record ShelfLifeDto(ShelfLifeStatus Status, decimal HoursLeft, decimal RemainingFraction)
{
    public static ShelfLifeDto From(ShelfLifeInfo info) => new(info.Status, info.HoursLeft, info.RemainingFraction);
}

/// <param name="FefoRank">Очерёдность списания по FEFO среди пригодных партий продукта (1 — первая); null для непригодных.</param>
public sealed record BatchDto(
    Guid Id,
    Guid ProductId,
    string ProductName,
    MeasureUnit Unit,
    Guid SupplyId,
    string SupplyNumber,
    DateTimeOffset ReceivedAt,
    DateTimeOffset ExpiresAt,
    ExpirySource ExpirySource,
    decimal InitialQty,
    decimal RemainingQty,
    BatchStatus Status,
    bool IsUsable,
    ShelfLifeDto ShelfLife,
    int? FefoRank);

public sealed record ProductBatchesDto(StockItemDto Stock, IReadOnlyList<BatchDto> Batches);

/// <param name="ShelfStatuses">Фильтр по статусу срока (Fresh/Warning/Critical/Expired).</param>
/// <param name="ExpiringWithinHours">Только партии, истекающие не позже чем через N часов (включая уже истёкшие).</param>
/// <param name="IncludeClosed">Включать израсходованные и списанные партии.</param>
/// <param name="BatchIds">Только указанные партии (внутреннее использование: дельты real-time).</param>
public sealed record BatchQuery(
    Guid? ProductId = null,
    IReadOnlyCollection<ShelfLifeStatus>? ShelfStatuses = null,
    decimal? ExpiringWithinHours = null,
    bool IncludeClosed = false,
    int Limit = 200,
    IReadOnlyCollection<Guid>? BatchIds = null);
