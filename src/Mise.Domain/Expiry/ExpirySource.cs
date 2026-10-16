namespace Mise.Domain.Expiry;

/// <summary>Откуда взят срок годности партии (CLAUDE.md, правило 1).</summary>
public enum ExpirySource
{
    ExplicitDate,
    SupplyItemShelfLife,
    ProductDefault,
    /// <summary>Заготовка: срок хранения от приготовления, но не дольше самого раннего срока взятых продуктов.</summary>
    Prep,
}

public readonly record struct ExpiryResolution(DateTimeOffset ExpiresAt, ExpirySource Source);
