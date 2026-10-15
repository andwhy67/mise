using FluentValidation;
using FluentValidation.Results;
using Microsoft.EntityFrameworkCore;
using Mise.Application.Abstractions;
using Mise.Application.Common;
using Mise.Domain.Abstractions;
using Mise.Domain.Catalog;
using Mise.Domain.Common;

namespace Mise.Application.Catalog;

public sealed class ProductService(IMiseDbContext db, IClock clock, IValidator<UpsertProductRequest> validator)
{
    public async Task<IReadOnlyList<ProductDto>> ListAsync(CancellationToken ct)
    {
        var products = await db.Products.AsNoTracking().Include(p => p.Substitutes).OrderBy(p => p.Name).ToListAsync(ct);
        var names = products.ToDictionary(p => p.Id, p => (p.Name, p.Unit));
        return products.Select(p => ProductDto.From(p, names)).ToList();
    }

    public async Task<ProductDto> GetAsync(Guid id, CancellationToken ct) =>
        await ToDtoAsync(await db.Products.AsNoTracking().Include(p => p.Substitutes).FirstOrDefaultAsync(p => p.Id == id, ct)
            ?? throw new NotFoundException("Продукт", id), ct);

    public async Task<ProductDto> CreateAsync(UpsertProductRequest request, CancellationToken ct)
    {
        await validator.EnsureValidAsync(request, ct);
        var name = request.Name.Trim();
        await EnsureNameIsFreeAsync(name, exceptId: null, ct);

        var product = new Product(Guid.CreateVersion7(clock.UtcNow), name, request.Unit);
        Apply(product, request);
        await ApplySubstitutesAsync(product, request, ct);
        db.Products.Add(product);
        await SaveAsync(ct);
        return await ToDtoAsync(product, ct);
    }

    public async Task<ProductDto> UpdateAsync(Guid id, UpsertProductRequest request, CancellationToken ct)
    {
        await validator.EnsureValidAsync(request, ct);
        var product = await db.Products.Include(p => p.Substitutes).FirstOrDefaultAsync(p => p.Id == id, ct)
            ?? throw new NotFoundException("Продукт", id);
        var name = request.Name.Trim();
        await EnsureNameIsFreeAsync(name, exceptId: id, ct);

        // Количества в партиях и составах записаны в старой единице — смена единицы сломала бы их смысл.
        if (product.Unit != request.Unit && await IsReferencedAsync(id, ct))
            throw new ConflictException("product.unit_locked",
                "Нельзя сменить единицу измерения продукта, который уже есть в поставках, партиях или составах блюд.");

        product.Name = name;
        product.Unit = request.Unit;
        Apply(product, request);
        await ApplySubstitutesAsync(product, request, ct);
        await SaveAsync(ct);
        return await ToDtoAsync(product, ct);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct)
    {
        var product = await db.Products.FirstOrDefaultAsync(p => p.Id == id, ct) ?? throw new NotFoundException("Продукт", id);
        if (await IsReferencedAsync(id, ct))
            throw new ConflictException("product.in_use",
                "Продукт используется в поставках, партиях или составах блюд — удалить его нельзя.");

        db.Products.Remove(product);
        await db.SaveChangesAsync(ct);
    }

    private static void Apply(Product product, UpsertProductRequest request)
    {
        product.DefaultShelfLifeHours = request.DefaultShelfLifeHours;
        product.MinStockQty = request.MinStockQty;
        product.PackSize = request.PackSize;
        product.Category = string.IsNullOrWhiteSpace(request.Category) ? null : request.Category.Trim();
        product.ReferenceUnitCost = request.ReferencePrice is { } price ? Prices.ToBase(price, request.Unit) : null;
        product.Supplier = string.IsNullOrWhiteSpace(request.Supplier) ? null : request.Supplier.Trim();
        product.StorageType = string.IsNullOrWhiteSpace(request.StorageType) ? null : request.StorageType.Trim();
    }

    /// <summary>Замены: только существующие продукты и не сам продукт; null в запросе — оставить как есть.</summary>
    private async Task ApplySubstitutesAsync(Product product, UpsertProductRequest request, CancellationToken ct)
    {
        if (request.Substitutes is not { } substitutes)
            return;
        var ids = substitutes.Select(s => s.SubstituteId).ToList();
        var known = await db.Products.Where(p => ids.Contains(p.Id)).Select(p => p.Id).ToListAsync(ct);
        var failures = substitutes
            .Select((s, index) => (s, index))
            .Where(x => x.s.SubstituteId == product.Id || !known.Contains(x.s.SubstituteId))
            .Select(x => new ValidationFailure($"Substitutes[{x.index}].SubstituteId",
                x.s.SubstituteId == product.Id ? "Продукт не может заменять сам себя." : "Продукт-заменитель не найден."))
            .ToList();
        if (failures.Count > 0)
            throw failures.ToValidationException();
        product.SetSubstitutes(substitutes.Select(s => (s.SubstituteId, s.Ratio)));
    }

    private async Task<ProductDto> ToDtoAsync(Product product, CancellationToken ct)
    {
        var ids = product.Substitutes.Select(s => s.SubstituteId).ToList();
        var names = await db.Products.AsNoTracking().Where(p => ids.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => (p.Name, p.Unit), ct);
        return ProductDto.From(product, names);
    }

    private async Task EnsureNameIsFreeAsync(string name, Guid? exceptId, CancellationToken ct)
    {
        var lower = name.ToLower();
        if (await db.Products.AnyAsync(p => p.Name.ToLower() == lower && p.Id != exceptId, ct))
            throw new ConflictException("product.duplicate_name", $"Продукт «{name}» уже есть в словаре.");
    }

    private async Task<bool> IsReferencedAsync(Guid id, CancellationToken ct) =>
        await db.DishIngredients.AnyAsync(i => i.ProductId == id, ct)
        || await db.SupplyItems.AnyAsync(i => i.ProductId == id, ct)
        || await db.Batches.AnyAsync(b => b.ProductId == id, ct);

    private async Task SaveAsync(CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (UniqueConstraintViolationException)
        {
            // Гонка двух одинаковых созданий: проверка имени прошла у обоих, индекс остановил второго.
            throw new ConflictException("product.duplicate_name", "Продукт с таким названием уже есть в словаре.");
        }
    }
}
