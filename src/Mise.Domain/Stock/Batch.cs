using Mise.Domain.Common;
using Mise.Domain.Expiry;

namespace Mise.Domain.Stock;

public enum BatchStatus
{
    /// <summary>На складе; пригодность определяется по expires_at.</summary>
    Active,
    /// <summary>Израсходована полностью.</summary>
    Depleted,
    /// <summary>Срок истёк, остаток ещё не списан.</summary>
    Expired,
    /// <summary>Списана (автоматически по сроку или вручную).</summary>
    WrittenOff,
}

/// <summary>Партия продукта из конкретной позиции поставки со своим сроком годности.</summary>
public sealed class Batch
{
    public static class Errors
    {
        public const string InvalidQuantity = "batch.invalid_quantity";
    }

    private Batch() { }

    public Guid Id { get; private set; }
    public Guid SupplyItemId { get; private set; }
    public Guid ProductId { get; private set; }
    public DateTimeOffset ReceivedAt { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }
    public ExpirySource ExpirySource { get; private set; }
    public decimal InitialQty { get; private set; }
    public decimal RemainingQty { get; private set; }
    public BatchStatus Status { get; private set; }

    public static Batch Receive(
        Guid id,
        Guid supplyItemId,
        Guid productId,
        DateTimeOffset receivedAt,
        ExpiryResolution expiry,
        decimal quantity,
        DateTimeOffset now)
    {
        if (quantity <= 0)
            throw new DomainException(Errors.InvalidQuantity, "Количество в партии должно быть больше нуля.");

        return new Batch
        {
            Id = id,
            SupplyItemId = supplyItemId,
            ProductId = productId,
            ReceivedAt = receivedAt.ToUniversalTime(),
            ExpiresAt = expiry.ExpiresAt,
            ExpirySource = expiry.Source,
            InitialQty = quantity,
            RemainingQty = quantity,
            // Партия, срок которой истёк ещё до регистрации, сразу помечается Expired (assumptions A2).
            Status = now >= expiry.ExpiresAt ? BatchStatus.Expired : BatchStatus.Active,
        };
    }

    /// <summary>Партия участвует в заказах и в доступном остатке (CLAUDE.md, правило 2).</summary>
    public bool IsUsableAt(DateTimeOffset now) => Status == BatchStatus.Active && RemainingQty > 0 && now < ExpiresAt;

    public decimal UsableQtyAt(DateTimeOffset now) => IsUsableAt(now) ? RemainingQty : 0m;
}
