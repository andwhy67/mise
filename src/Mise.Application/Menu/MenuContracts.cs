using Mise.Domain.Common;
using Mise.Domain.Menu;

namespace Mise.Application.Menu;

public sealed record IngredientAvailabilityDto(
    Guid ProductId,
    string ProductName,
    MeasureUnit Unit,
    decimal QtyPerPortion,
    decimal Usable,
    int Portions);

/// <param name="MaxPortions">Сколько порций можно приготовить из пригодного (непросроченного) остатка.</param>
/// <param name="LimitingProduct">Ингредиент, на котором достигнут минимум порций.</param>
public sealed record DishAvailabilityDto(
    Guid DishId,
    string DishName,
    string? Category,
    decimal Price,
    bool IsActive,
    int MaxPortions,
    DishAvailabilityStatus Status,
    bool InStopList,
    IngredientAvailabilityDto? LimitingProduct,
    IReadOnlyList<IngredientAvailabilityDto> Ingredients);

/// <param name="Since">Когда блюдо попало в стоп (по истории); null, если переход ещё не зафиксирован.</param>
public sealed record StopListItemDto(
    Guid DishId,
    string DishName,
    string? Category,
    StopReason Reason,
    IngredientAvailabilityDto? LimitingProduct,
    DateTimeOffset? Since);

public sealed record StopListHistoryItemDto(
    Guid Id,
    Guid DishId,
    string DishName,
    StopReason Reason,
    Guid? LimitingProductId,
    string? LimitingProductName,
    DateTimeOffset StartedAt,
    DateTimeOffset? EndedAt,
    decimal? DurationMinutes);

/// <summary>Изменения стоп-листа после пересчёта (пригодится для real-time в фазе 4).</summary>
public sealed record StopListChanges(IReadOnlyList<Guid> Added, IReadOnlyList<Guid> Removed)
{
    public static StopListChanges None { get; } = new([], []);
    public bool IsEmpty => Added.Count == 0 && Removed.Count == 0;
}
