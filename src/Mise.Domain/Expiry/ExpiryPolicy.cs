using Mise.Domain.Common;

namespace Mise.Domain.Expiry;

/// <summary>
/// Расчёт expires_at партии: явная дата → срок позиции от даты поставки → срок по умолчанию из словаря → ошибка.
/// </summary>
public static class ExpiryPolicy
{
    public static class Errors
    {
        public const string Unresolvable = "expiry.unresolvable";
        public const string NotAfterReceipt = "expiry.not_after_receipt";
        public const string InvalidShelfLife = "expiry.invalid_shelf_life";
    }

    public static ExpiryResolution Resolve(
        DateTimeOffset receivedAt,
        DateTimeOffset? explicitExpiresAt,
        int? itemShelfLifeHours,
        int? productDefaultShelfLifeHours)
    {
        receivedAt = receivedAt.ToUniversalTime();

        if (explicitExpiresAt is { } explicitDate)
        {
            var expiresAt = explicitDate.ToUniversalTime();
            if (expiresAt <= receivedAt)
                throw new DomainException(Errors.NotAfterReceipt, "Срок годности должен быть позже даты поставки.");
            return new ExpiryResolution(expiresAt, ExpirySource.ExplicitDate);
        }

        if (itemShelfLifeHours is { } itemHours)
            return new ExpiryResolution(receivedAt.AddHours(EnsurePositive(itemHours)), ExpirySource.SupplyItemShelfLife);

        if (productDefaultShelfLifeHours is { } defaultHours)
            return new ExpiryResolution(receivedAt.AddHours(EnsurePositive(defaultHours)), ExpirySource.ProductDefault);

        throw new DomainException(Errors.Unresolvable,
            "Не удалось определить срок годности: нет ни даты, ни срока хранения в позиции, ни срока по умолчанию у продукта.");
    }

    private static int EnsurePositive(int hours) => hours > 0
        ? hours
        : throw new DomainException(Errors.InvalidShelfLife, "Срок хранения должен быть больше нуля.");
}
