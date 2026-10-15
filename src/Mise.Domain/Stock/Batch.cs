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
        public const string InvalidCost = "batch.invalid_cost";
        public const string InsufficientQuantity = "batch.insufficient_quantity";
        public const string NotUsable = "batch.not_usable";
        public const string NotReturnable = "batch.not_returnable";
        public const string NotWritable = "batch.not_writable";
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
    /// <summary>Цена закупки за базовую единицу (₽ за г / мл / шт); null — цена неизвестна.</summary>
    public decimal? UnitCost { get; private set; }

    public static Batch Receive(
        Guid id,
        Guid supplyItemId,
        Guid productId,
        DateTimeOffset receivedAt,
        ExpiryResolution expiry,
        decimal quantity,
        DateTimeOffset now,
        decimal? unitCost = null)
    {
        if (quantity <= 0)
            throw new DomainException(Errors.InvalidQuantity, "Количество в партии должно быть больше нуля.");
        if (unitCost < 0)
            throw new DomainException(Errors.InvalidCost, "Цена закупки не может быть отрицательной.");

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
            UnitCost = unitCost,
            // Партия, срок которой истёк ещё до регистрации, сразу помечается Expired (assumptions A2).
            Status = now >= expiry.ExpiresAt ? BatchStatus.Expired : BatchStatus.Active,
        };
    }

    /// <summary>Стоимость количества по цене партии, до копеек; null — цена партии неизвестна.</summary>
    public decimal? CostOf(decimal quantity) => UnitCost is { } price ? decimal.Round(quantity * price, 2, MidpointRounding.AwayFromZero) : null;

    /// <summary>Партия участвует в заказах и в доступном остатке (CLAUDE.md, правило 2).</summary>
    public bool IsUsableAt(DateTimeOffset now) => Status == BatchStatus.Active && RemainingQty > 0 && now < ExpiresAt;

    public decimal UsableQtyAt(DateTimeOffset now) => IsUsableAt(now) ? RemainingQty : 0m;

    /// <summary>Списание под заказ. Только из пригодной партии и не больше остатка — остаток не уходит в минус.</summary>
    public void Consume(decimal quantity, DateTimeOffset now)
    {
        if (quantity <= 0)
            throw new DomainException(Errors.InvalidQuantity, "Количество для списания должно быть больше нуля.");
        if (!IsUsableAt(now))
            throw new DomainException(Errors.NotUsable, "Партия непригодна для использования (просрочена или израсходована).");
        if (quantity > RemainingQty)
            throw new DomainException(Errors.InsufficientQuantity,
                $"В партии осталось {RemainingQty}, а требуется списать {quantity}.");

        RemainingQty -= quantity;
        if (RemainingQty == 0)
            Status = BatchStatus.Depleted;
    }

    /// <summary>Перевод активной партии в Expired, когда срок наступил. Возвращает true, если статус изменился.</summary>
    public bool MarkExpired(DateTimeOffset now)
    {
        if (Status != BatchStatus.Active || RemainingQty <= 0 || now < ExpiresAt)
            return false;
        Status = BatchStatus.Expired;
        return true;
    }

    /// <summary>Списать можно только то, что физически лежит на складе: активную или просроченную партию с остатком.</summary>
    public bool CanBeWrittenOff => Status is BatchStatus.Active or BatchStatus.Expired && RemainingQty > 0;

    /// <summary>
    /// Списание (по сроку или вручную): <paramref name="quantity"/> = null — весь остаток.
    /// Обнулённая партия получает статус WrittenOff. Возвращает списанное количество.
    /// </summary>
    public decimal WriteOff(decimal? quantity)
    {
        if (!CanBeWrittenOff)
            throw new DomainException(Errors.NotWritable, "Партия уже израсходована или списана — списывать нечего.");
        var qty = quantity ?? RemainingQty;
        if (qty <= 0)
            throw new DomainException(Errors.InvalidQuantity, "Количество для списания должно быть больше нуля.");
        if (qty > RemainingQty)
            throw new DomainException(Errors.InsufficientQuantity,
                $"В партии осталось {RemainingQty}, а требуется списать {qty}.");

        RemainingQty -= qty;
        if (RemainingQty == 0)
            Status = BatchStatus.WrittenOff;
        return qty;
    }

    /// <summary>Возврат допустим только в непросроченную и не списанную партию.</summary>
    public bool CanAcceptReturnAt(DateTimeOffset now) =>
        Status is BatchStatus.Active or BatchStatus.Depleted && now < ExpiresAt;

    /// <summary>Компенсация отменённого заказа: возврат в ту же партию.</summary>
    public void Return(decimal quantity, DateTimeOffset now)
    {
        if (quantity <= 0 || RemainingQty + quantity > InitialQty)
            throw new DomainException(Errors.InvalidQuantity, "Возврат не может превышать изначальное количество партии.");
        if (!CanAcceptReturnAt(now))
            throw new DomainException(Errors.NotReturnable, "Партия просрочена или списана — вернуть в неё нельзя.");

        RemainingQty += quantity;
        Status = BatchStatus.Active;
    }
}
