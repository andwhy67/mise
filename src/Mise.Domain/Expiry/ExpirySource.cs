namespace Mise.Domain.Expiry;

/// <summary>Откуда взят срок годности партии (CLAUDE.md, правило 1).</summary>
public enum ExpirySource
{
    ExplicitDate,
    SupplyItemShelfLife,
    ProductDefault,
}

public readonly record struct ExpiryResolution(DateTimeOffset ExpiresAt, ExpirySource Source);
