using Mise.Domain.Common;

namespace Mise.Domain.Restaurants;

/// <summary>
/// Ресторан — граница данных: продукты, склад, заказы и сотрудники принадлежат ровно одному ресторану
/// и не видны другим. Одинаковые названия у разных ресторанов допустимы: это разные организации.
/// </summary>
public sealed class Restaurant
{
    public static class Errors
    {
        public const string InvalidName = "restaurant.invalid_name";
    }

    public const int NameMaxLength = 120;

    private Restaurant() { }

    public Restaurant(Guid id, string name, DateTimeOffset createdAt, bool isDemo = false)
    {
        Id = id;
        Rename(name);
        CreatedAt = createdAt;
        IsDemo = isDemo;
    }

    public Guid Id { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; private set; }
    /// <summary>Демо-ресторан: в нём живут демо-учётки и работает симулятор.</summary>
    public bool IsDemo { get; private set; }
    /// <summary>Последний выданный номер заказа — нумерация своя у каждого ресторана.</summary>
    public long LastOrderNumber { get; private set; }

    public void Rename(string name)
    {
        var trimmed = name?.Trim() ?? string.Empty;
        if (trimmed.Length is 0 or > NameMaxLength)
            throw new DomainException(Errors.InvalidName, $"Название ресторана — от 1 до {NameMaxLength} символов.");
        Name = trimmed;
    }
}
