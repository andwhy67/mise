namespace Mise.Simulator.Model;

// Модель каталога, как её видит внешний клиент API (только нужные поля ответов /products, /dishes, /stock).

public sealed record ProductInfo(
    Guid Id,
    string Name,
    string Unit,
    int? DefaultShelfLifeHours,
    decimal MinStockQty,
    decimal PackSize);

public sealed record DishIngredientInfo(Guid ProductId, decimal Quantity);

public sealed record DishInfo(Guid Id, string Name, string? Category, decimal Price, bool IsActive, IReadOnlyList<DishIngredientInfo> Ingredients);

public sealed record StockInfo(Guid ProductId, decimal UsableQty);

public sealed record OrderLine(Guid DishId, int Quantity);

/// <param name="ShelfLifeHours">Срок от даты поставки (часы) — обычные поставки.</param>
/// <param name="ExpiresAt">Явная дата истечения — короткие сроки сценария Spoilage.</param>
public sealed record SupplyLine(Guid ProductId, decimal Quantity, int? ShelfLifeHours, DateTimeOffset? ExpiresAt);

public sealed record SupplyDraft(string Number, string Supplier, IReadOnlyList<SupplyLine> Items);
