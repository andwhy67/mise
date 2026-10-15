using FluentValidation;
using FluentValidation.Results;
using Microsoft.EntityFrameworkCore;
using Mise.Application.Abstractions;
using Mise.Application.Common;
using Mise.Application.Menu;
using Mise.Application.Realtime;
using Mise.Domain.Abstractions;
using Mise.Domain.Catalog;

namespace Mise.Application.Catalog;

public sealed class DishService(
    IMiseDbContext db,
    IClock clock,
    StopListService stopList,
    IRealtimeNotifier notifier,
    IValidator<CreateDishRequest> createValidator,
    IValidator<UpdateDishRequest> updateValidator,
    IValidator<SetDishIngredientsRequest> ingredientsValidator)
{
    public async Task<IReadOnlyList<DishDto>> ListAsync(CancellationToken ct)
    {
        var dishes = await db.Dishes.AsNoTracking().Include(d => d.Ingredients).OrderBy(d => d.Name).ToListAsync(ct);
        var products = await LoadProductsAsync(dishes.SelectMany(d => d.Ingredients).Select(i => i.ProductId), ct);
        return dishes.Select(d => ToDto(d, products)).ToList();
    }

    public async Task<DishDto> GetAsync(Guid id, CancellationToken ct)
    {
        var dish = await db.Dishes.AsNoTracking().Include(d => d.Ingredients).FirstOrDefaultAsync(d => d.Id == id, ct)
            ?? throw new NotFoundException("Блюдо", id);
        return ToDto(dish, await LoadProductsAsync(dish.Ingredients.Select(i => i.ProductId), ct));
    }

    public async Task<DishDto> CreateAsync(CreateDishRequest request, CancellationToken ct)
    {
        await createValidator.EnsureValidAsync(request, ct);
        var name = request.Name.Trim();
        await EnsureNameIsFreeAsync(name, exceptId: null, ct);
        var products = await RequireProductsAsync(request.Ingredients, ct);

        var dish = new Dish(Guid.CreateVersion7(clock.UtcNow), name)
        {
            Category = Normalize(request.Category),
            Price = request.Price,
            IsActive = request.IsActive,
        };
        dish.SetIngredients(request.Ingredients.Select(i => (i.ProductId, i.Quantity, i.Removable)));
        db.Dishes.Add(dish);
        await SaveAsync(ct);
        notifier.DishesChanged([dish.Id]);
        await stopList.TryRefreshAsync(ct);
        return ToDto(dish, products);
    }

    public async Task<DishDto> UpdateAsync(Guid id, UpdateDishRequest request, CancellationToken ct)
    {
        await updateValidator.EnsureValidAsync(request, ct);
        var dish = await db.Dishes.Include(d => d.Ingredients).FirstOrDefaultAsync(d => d.Id == id, ct)
            ?? throw new NotFoundException("Блюдо", id);
        var name = request.Name.Trim();
        await EnsureNameIsFreeAsync(name, exceptId: id, ct);

        dish.Name = name;
        dish.Category = Normalize(request.Category);
        dish.Price = request.Price;
        dish.IsActive = request.IsActive;
        await SaveAsync(ct);
        notifier.DishesChanged([dish.Id]);
        await stopList.TryRefreshAsync(ct);
        return ToDto(dish, await LoadProductsAsync(dish.Ingredients.Select(i => i.ProductId), ct));
    }

    public async Task<DishDto> SetIngredientsAsync(Guid id, SetDishIngredientsRequest request, CancellationToken ct)
    {
        await ingredientsValidator.EnsureValidAsync(request, ct);
        var dish = await db.Dishes.Include(d => d.Ingredients).FirstOrDefaultAsync(d => d.Id == id, ct)
            ?? throw new NotFoundException("Блюдо", id);
        var products = await RequireProductsAsync(request.Ingredients, ct);

        dish.SetIngredients(request.Ingredients.Select(i => (i.ProductId, i.Quantity, i.Removable)));
        await db.SaveChangesAsync(ct);
        notifier.DishesChanged([dish.Id]);
        await stopList.TryRefreshAsync(ct);
        return ToDto(dish, products);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct)
    {
        var dish = await db.Dishes.FirstOrDefaultAsync(d => d.Id == id, ct) ?? throw new NotFoundException("Блюдо", id);
        if (await db.OrderItems.AnyAsync(i => i.DishId == id, ct))
            throw new ConflictException("dish.in_use",
                "По блюду уже есть заказы — удалить его нельзя. Выключите блюдо из меню (isActive = false).");
        db.Dishes.Remove(dish);
        await db.SaveChangesAsync(ct);
    }

    private async Task<Dictionary<Guid, Product>> RequireProductsAsync(IReadOnlyList<DishIngredientRequest> ingredients, CancellationToken ct)
    {
        var products = await LoadProductsAsync(ingredients.Select(i => i.ProductId), ct);
        var failures = ingredients
            .Select((item, index) => (item, index))
            .Where(x => !products.ContainsKey(x.item.ProductId))
            .Select(x => new ValidationFailure($"Ingredients[{x.index}].ProductId", $"Продукт «{x.item.ProductId}» не найден в словаре."))
            .ToList();
        if (failures.Count > 0)
            throw failures.ToValidationException();
        return products;
    }

    private async Task<Dictionary<Guid, Product>> LoadProductsAsync(IEnumerable<Guid> ids, CancellationToken ct)
    {
        var distinct = ids.Distinct().ToList();
        return await db.Products.AsNoTracking().Where(p => distinct.Contains(p.Id)).ToDictionaryAsync(p => p.Id, ct);
    }

    private static DishDto ToDto(Dish dish, IReadOnlyDictionary<Guid, Product> products) => new(
        dish.Id,
        dish.Name,
        dish.Category,
        dish.Price,
        dish.IsActive,
        dish.Ingredients
            .Select(i => products.TryGetValue(i.ProductId, out var p)
                ? new DishIngredientDto(i.ProductId, p.Name, p.Unit, i.Quantity, i.Removable)
                : new DishIngredientDto(i.ProductId, "?", default, i.Quantity, i.Removable))
            .OrderBy(i => i.ProductName)
            .ToList());

    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private async Task EnsureNameIsFreeAsync(string name, Guid? exceptId, CancellationToken ct)
    {
        var lower = name.ToLower();
        if (await db.Dishes.AnyAsync(d => d.Name.ToLower() == lower && d.Id != exceptId, ct))
            throw new ConflictException("dish.duplicate_name", $"Блюдо «{name}» уже есть в меню.");
    }

    private async Task SaveAsync(CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (UniqueConstraintViolationException)
        {
            throw new ConflictException("dish.duplicate_name", "Блюдо с таким названием уже есть в меню.");
        }
    }
}
