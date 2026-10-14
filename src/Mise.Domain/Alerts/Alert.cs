using Mise.Domain.Common;
using Mise.Domain.Expiry;

namespace Mise.Domain.Alerts;

public enum AlertType
{
    /// <summary>Партия пересекла порог Warning/Critical по сроку.</summary>
    ExpiringSoon,
    /// <summary>Срок партии истёк.</summary>
    Expired,
    /// <summary>Доступный остаток ниже минимального.</summary>
    LowStock,
    /// <summary>Блюдо вошло в стоп-лист.</summary>
    StopListOn,
    /// <summary>Блюдо вышло из стоп-листа.</summary>
    StopListOff,
}

public enum AlertSeverity
{
    Info,
    Warning,
    Critical,
}

/// <summary>Ключи дедупликации: один алерт на партию и уровень, один открытый LowStock на продукт.</summary>
public static class AlertKeys
{
    public static string Batch(Guid batchId, ShelfLifeStatus level) => $"batch:{batchId:N}:{level}";

    public static string LowStock(Guid productId) => $"lowstock:{productId:N}";
}

/// <summary>
/// Сигнал для персонала. «Состояния» (ExpiringSoon, LowStock) закрываются (<see cref="ResolvedAt"/>), когда причина ушла;
/// «события» (Expired, StopListOn/Off) не закрываются — только подтверждаются (<see cref="AcknowledgedAt"/>).
/// </summary>
public sealed class Alert
{
    private Alert() { }

    public Guid Id { get; private set; }
    public AlertType Type { get; private set; }
    public AlertSeverity Severity { get; private set; }
    public Guid? BatchId { get; private set; }
    public Guid? ProductId { get; private set; }
    public Guid? DishId { get; private set; }
    public string Message { get; private set; } = string.Empty;
    public string? DedupKey { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? AcknowledgedAt { get; private set; }
    public DateTimeOffset? ResolvedAt { get; private set; }

    // Структурные детали: интерфейс собирает фразу на языке пользователя, Message — готовый текст на русском.
    /// <summary>Количество: остаток партии (Expired) или доступный остаток (LowStock).</summary>
    public decimal? Quantity { get; private set; }
    /// <summary>Минимальный остаток (LowStock).</summary>
    public decimal? LimitQty { get; private set; }
    /// <summary>Когда истекает партия (ExpiringSoon, Expired).</summary>
    public DateTimeOffset? ExpiresAt { get; private set; }
    /// <summary>Остаток списан автоматически (Expired).</summary>
    public bool? AutoWrittenOff { get; private set; }

    public bool IsActive => ResolvedAt is null;

    public static Alert ExpiringSoon(Guid id, Guid batchId, Guid productId, string productName, ShelfLifeStatus level,
        decimal hoursLeft, DateTimeOffset expiresAt, DateTimeOffset now)
    {
        var severity = level switch
        {
            ShelfLifeStatus.Warning => AlertSeverity.Warning,
            ShelfLifeStatus.Critical => AlertSeverity.Critical,
            _ => throw new ArgumentOutOfRangeException(nameof(level), level, "ExpiringSoon — только для Warning и Critical."),
        };
        return new Alert
        {
            Id = id,
            Type = AlertType.ExpiringSoon,
            Severity = severity,
            BatchId = batchId,
            ProductId = productId,
            Message = $"{productName}: партия истекает через {RuFormat.Hours(hoursLeft)}.",
            DedupKey = AlertKeys.Batch(batchId, level),
            ExpiresAt = expiresAt,
            CreatedAt = now,
        };
    }

    /// <param name="quantity">Остаток партии в момент истечения.</param>
    /// <param name="writtenOff">Остаток уже списан автоматически (иначе — лежит на складе и требует действия).</param>
    public static Alert Expired(Guid id, Guid batchId, Guid productId, string productName, decimal quantity, MeasureUnit unit,
        bool writtenOff, DateTimeOffset expiresAt, DateTimeOffset now) => new()
    {
        Id = id,
        Type = AlertType.Expired,
        Severity = writtenOff ? AlertSeverity.Warning : AlertSeverity.Critical,
        BatchId = batchId,
        ProductId = productId,
        Message = writtenOff
            ? $"{productName}: срок партии истёк, остаток {RuFormat.Quantity(quantity, unit)} списан автоматически."
            : $"{productName}: срок партии истёк, остаток {RuFormat.Quantity(quantity, unit)} нужно списать.",
        DedupKey = AlertKeys.Batch(batchId, ShelfLifeStatus.Expired),
        Quantity = quantity,
        ExpiresAt = expiresAt,
        AutoWrittenOff = writtenOff,
        CreatedAt = now,
    };

    public static Alert LowStock(Guid id, Guid productId, string productName, decimal usable, decimal minStock, MeasureUnit unit,
        DateTimeOffset now) => new()
    {
        Id = id,
        Type = AlertType.LowStock,
        Severity = usable <= 0 ? AlertSeverity.Critical : AlertSeverity.Warning,
        ProductId = productId,
        Message = usable <= 0
            ? $"{productName}: доступного остатка нет (минимум {RuFormat.Quantity(minStock, unit)})."
            : $"{productName}: доступно {RuFormat.Quantity(usable, unit)} при минимуме {RuFormat.Quantity(minStock, unit)}.",
        DedupKey = AlertKeys.LowStock(productId),
        Quantity = usable,
        LimitQty = minStock,
        CreatedAt = now,
    };

    /// <param name="limitingProductId">Продукт, которого не хватило (ProductId алерта); null — блюдо выключено вручную.</param>
    public static Alert StopListOn(Guid id, Guid dishId, string dishName, Guid? limitingProductId, string? limitingProductName,
        DateTimeOffset now) => new()
    {
        Id = id,
        Type = AlertType.StopListOn,
        Severity = AlertSeverity.Warning,
        DishId = dishId,
        ProductId = limitingProductId,
        Message = limitingProductName is null
            ? $"«{dishName}» в стоп-листе."
            : $"«{dishName}» в стоп-листе: не хватает «{limitingProductName}».",
        CreatedAt = now,
    };

    public static Alert StopListOff(Guid id, Guid dishId, string dishName, DateTimeOffset now) => new()
    {
        Id = id,
        Type = AlertType.StopListOff,
        Severity = AlertSeverity.Info,
        DishId = dishId,
        Message = $"«{dishName}» снова доступно.",
        CreatedAt = now,
    };

    public void Acknowledge(DateTimeOffset now) => AcknowledgedAt ??= now;

    public void Resolve(DateTimeOffset now) => ResolvedAt ??= now;
}
