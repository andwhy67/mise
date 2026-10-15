namespace Mise.Application.Abstractions;

/// <summary>
/// Текущий ресторан — граница данных. В запросе берётся из токена (claim <c>rid</c>), в фоновых задачах
/// задаётся явно через <see cref="Use"/> для каждого ресторана по очереди. Без ресторана запросы к данным
/// ресторана пусты, а сохранение таких данных — ошибка: «ничей» продукт или заказ появиться не может.
/// </summary>
public interface ITenant
{
    Guid? RestaurantId { get; }

    /// <summary>Работать от имени ресторана до Dispose (вложенные вызовы восстанавливают прежний).</summary>
    IDisposable Use(Guid restaurantId);
}
