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
        public const string InvalidTables = "restaurant.invalid_tables";
    }

    public const int NameMaxLength = 120;
    public const int MenuKeyLength = 12;
    public const int MaxTables = 200;

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

    /// <summary>
    /// Ключ гостевого меню (QR на столах ведёт на /m/{ключ}): постоянный, чтобы не перепечатывать QR при смене меню.
    /// null — меню ещё не включали. Перевыпуск делает старые QR недействительными.
    /// </summary>
    public string? MenuKey { get; private set; }
    /// <summary>Гости могут не только смотреть меню, но и заказывать.</summary>
    public bool GuestOrdering { get; private set; } = true;
    /// <summary>Сколько столов: QR печатается для каждого, номер стола приходит на кухню.</summary>
    public int GuestTables { get; private set; } = 10;

    public void SetMenuKey(string key) => MenuKey = key;

    public void ConfigureGuestMenu(bool ordering, int tables)
    {
        if (tables is < 0 or > MaxTables)
            throw new DomainException(Errors.InvalidTables, $"Столов — от 0 до {MaxTables}.");
        GuestOrdering = ordering;
        GuestTables = tables;
    }

    public void Rename(string name)
    {
        var trimmed = name?.Trim() ?? string.Empty;
        if (trimmed.Length is 0 or > NameMaxLength)
            throw new DomainException(Errors.InvalidName, $"Название ресторана — от 1 до {NameMaxLength} символов.");
        Name = trimmed;
    }
}
