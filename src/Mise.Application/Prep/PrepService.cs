using FluentValidation;
using FluentValidation.Results;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Mise.Application.Abstractions;
using Mise.Application.Common;
using Mise.Application.Menu;
using Mise.Application.Realtime;
using Mise.Application.Stock;
using Mise.Domain.Abstractions;
using Mise.Domain.Catalog;
using Mise.Domain.Common;
using Mise.Domain.Expiry;
using Mise.Domain.Prep;
using Mise.Domain.Stock;
using Mise.Domain.Supplies;

namespace Mise.Application.Prep;

/// <summary>
/// Заготовки (соус, бульон): ингредиенты списываются по FEFO под блокировкой партий, а на склад приходит партия заготовки
/// через внутреннюю поставку «ЗАГ-…» — дальше её видят остатки, FEFO, сроки, стоп-лист и заказы как любой продукт.
/// </summary>
public sealed class PrepService(
    IMiseDbContext db,
    IStockLocks locks,
    IClock clock,
    StopListService stopList,
    IRealtimeNotifier notifier,
    IValidator<CookPrepRequest> validator,
    IOptions<StockOptions> stockOptions)
{
    public const string SupplierName = "Заготовка";

    public static class Errors
    {
        public const string InsufficientStock = "prep.insufficient_stock";
        public const string IdempotencyKeyReused = "prep.idempotency_key_reused";
    }

    public async Task<IReadOnlyList<PrepItemDto>> ListAsync(CancellationToken ct)
    {
        var now = clock.UtcNow;
        var preps = await db.Products.AsNoTracking().Include(p => p.PrepIngredients)
            .Where(p => p.PrepYield != null).OrderBy(p => p.Name).ToListAsync(ct);
        var usable = await MenuSnapshot.LoadUsableAsync(db, now, ct);
        return preps.Select(p =>
        {
            // Сколько выходов рецепта хватает по самому дефицитному ингредиенту.
            var yields = p.PrepIngredients.Min(i => usable.GetValueOrDefault(i.IngredientId) / i.Quantity);
            return new PrepItemDto(p.Id, p.Name, p.Unit, p.PrepYield!.Value, p.DefaultShelfLifeHours ?? 0,
                usable.GetValueOrDefault(p.Id), decimal.Round(yields * p.PrepYield!.Value, 0, MidpointRounding.ToZero));
        }).ToList();
    }

    /// <summary>Что будет, если приготовить: какие партии уйдут, срок и себестоимость заготовки. Ничего не меняет.</summary>
    public async Task<PrepPlanDto> PreviewAsync(Guid prepId, CookPrepRequest request, CancellationToken ct)
    {
        await validator.EnsureValidAsync(request, ct);
        var now = clock.UtcNow;
        var prep = await LoadPrepAsync(prepId, ct);
        var quantity = ToBase(request, prep);
        var ids = prep.PrepIngredients.Select(i => i.IngredientId).ToList();
        var batches = await db.Batches.AsNoTracking()
            .Where(b => ids.Contains(b.ProductId) && b.Status == BatchStatus.Active && b.RemainingQty > 0 && b.ExpiresAt > now)
            .ToListAsync(ct);
        return await ToDtoAsync(prep, PrepPlanner.Plan(prep, quantity, batches, now), batches, now, ct);
    }

    public async Task<PrepResultDto> CookAsync(Guid prepId, CookPrepRequest request, string? idempotencyKey, CancellationToken ct)
    {
        await validator.EnsureValidAsync(request, ct);
        var key = RequestHash.NormalizeKey(idempotencyKey);
        var hash = RequestHash.Compute(new { prepId, request });
        if (key is not null && await FindReplayAsync(key, hash, ct) is { } replay)
            return replay;

        var prep = await LoadPrepAsync(prepId, ct);
        var quantity = ToBase(request, prep);
        var ids = prep.PrepIngredients.Select(i => i.IngredientId).ToList();

        Supply supply;
        Batch output;
        PrepPlanDto dto;
        var depleted = new List<Guid>();
        await using (var transaction = await db.Database.BeginTransactionAsync(ct))
        {
            var now = clock.UtcNow;
            var batches = await locks.LockUsableBatchesAsync(ids, now, ct);
            var plan = PrepPlanner.Plan(prep, quantity, batches, now);
            dto = await ToDtoAsync(prep, plan, batches, now, ct);
            if (!plan.IsSatisfied)
            {
                await transaction.RollbackAsync(ct);
                var missing = dto.Ingredients.Where(i => i.Shortage > 0)
                    .Select(i => new { i.ProductId, i.ProductName, i.Unit, i.Required, i.Available, i.Shortage })
                    .ToList();
                throw new BusinessRuleException(Errors.InsufficientStock,
                    $"Не хватает продуктов, чтобы приготовить «{prep.Name}».",
                    new Dictionary<string, object?> { ["missing"] = missing });
            }

            // Номер — дата и случайный хвост id: две заготовки в одну миллисекунду не столкнутся.
            var supplyId = Guid.CreateVersion7(now);
            supply = new Supply(supplyId, $"ЗАГ-{now:yyMMdd}-{supplyId.ToString("N")[^6..].ToUpperInvariant()}", now, now)
            {
                SupplierName = SupplierName,
                IdempotencyKey = key,
                RequestHash = hash,
            };
            var item = new SupplyItem(Guid.CreateVersion7(now), supply.Id, 1, prep.Id, prep.Name, quantity)
            {
                ExpiresAt = plan.ExpiresAt,
                UnitCost = plan.UnitCost,
            };
            supply.AddItem(item);
            output = Batch.Receive(Guid.CreateVersion7(now), item.Id, prep.Id, now,
                new ExpiryResolution(plan.ExpiresAt, ExpirySource.Prep), quantity, now, plan.UnitCost);

            var byId = batches.ToDictionary(b => b.Id);
            foreach (var allocation in plan.Ingredients.SelectMany(i => i.Allocation.Allocations))
            {
                var batch = byId[allocation.BatchId];
                db.StockMovements.Add(StockMovement.PrepConsumption(Guid.CreateVersion7(now), batch, allocation.Quantity, supply.Id,
                    prep.Name, now, stockOptions.Value.Rescue));
                batch.Consume(allocation.Quantity, now);
                if (batch.Status == BatchStatus.Depleted)
                    depleted.Add(batch.Id);
            }

            db.Supplies.Add(supply);
            db.Batches.Add(output);
            db.StockMovements.Add(StockMovement.PrepOutput(Guid.CreateVersion7(now), output, supply.Id, now));
            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (UniqueConstraintViolationException) when (key is not null)
            {
                // Параллельный повтор с тем же ключом успел раньше — отдаём его результат.
                await transaction.RollbackAsync(ct);
                db.ChangeTracker.Clear();
                return await FindReplayAsync(key, hash, ct)
                    ?? throw new ConflictException(Errors.IdempotencyKeyReused, "Idempotency-Key уже использован для другой операции.");
            }
            await transaction.CommitAsync(ct);
        }

        notifier.SupplyReceived(supply.Id);
        notifier.StockChanged([.. ids, prep.Id]);
        notifier.BatchStatusChanged(depleted);
        await stopList.TryRefreshAsync(ct);
        return new PrepResultDto(supply.Id, supply.Number, output.Id, dto);
    }

    private async Task<Product> LoadPrepAsync(Guid id, CancellationToken ct)
    {
        var prep = await db.Products.AsNoTracking().Include(p => p.PrepIngredients).FirstOrDefaultAsync(p => p.Id == id, ct)
            ?? throw new NotFoundException("Продукт", id);
        return prep.IsPrep
            ? prep
            : throw new ConflictException(PrepPlanner.Errors.NotPrep, $"«{prep.Name}» — не заготовка: у продукта нет рецепта.");
    }

    private static decimal ToBase(CookPrepRequest request, Product prep)
    {
        try
        {
            return UnitConverter.ToBase(request.Quantity, request.Unit ?? UnitConverter.DefaultInputFor(prep.Unit), prep.Unit);
        }
        catch (DomainException ex)
        {
            throw new[] { new ValidationFailure(nameof(request.Quantity), ex.Message) { ErrorCode = ex.Code } }.ToValidationException();
        }
    }

    /// <summary>Тот же ключ и то же тело — прежний результат; тот же ключ с другим телом — 409.</summary>
    private async Task<PrepResultDto?> FindReplayAsync(string key, string hash, CancellationToken ct)
    {
        var supply = await db.Supplies.AsNoTracking().Include(s => s.Items).FirstOrDefaultAsync(s => s.IdempotencyKey == key, ct);
        if (supply is null)
            return null;
        if (supply.RequestHash != hash || supply.SupplierName != SupplierName)
            throw new ConflictException(Errors.IdempotencyKeyReused, "Idempotency-Key уже использован для другой операции.");

        var item = supply.Items.Single();
        var batch = await db.Batches.AsNoTracking().FirstAsync(b => b.SupplyItemId == item.Id, ct);
        var prep = await LoadPrepAsync(item.ProductId, ct);
        var plan = new PrepPlanDto(prep.Id, prep.Name, prep.Unit, batch.InitialQty, true, batch.ExpiresAt, prep.DefaultShelfLifeHours ?? 0,
            null, batch.CostOf(batch.InitialQty), 0m, []);
        return new PrepResultDto(supply.Id, supply.Number, batch.Id, plan);
    }

    private async Task<PrepPlanDto> ToDtoAsync(Product prep, PrepPlan plan, IReadOnlyCollection<Batch> batches, DateTimeOffset now, CancellationToken ct)
    {
        var ids = plan.Ingredients.Select(i => i.ProductId).ToList();
        var names = await db.Products.AsNoTracking().Where(p => ids.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => (p.Name, p.Unit), ct);
        var byId = batches.ToDictionary(b => b.Id);
        var rescue = stockOptions.Value.Rescue;
        var rescued = 0m;

        var ingredients = plan.Ingredients.Select(i =>
        {
            var uses = i.Allocation.Allocations.Select(a =>
            {
                var batch = byId[a.BatchId];
                var isRescued = rescue.Applies(batch, now);
                if (isRescued)
                    rescued += batch.CostOf(a.Quantity) ?? 0m;
                return new PrepBatchUseDto(a.BatchId, a.Quantity, batch.ExpiresAt, isRescued);
            }).ToList();
            return new PrepIngredientUseDto(i.ProductId, names[i.ProductId].Name, names[i.ProductId].Unit, i.Required,
                i.Allocation.Available, i.Allocation.Shortage, uses);
        }).OrderBy(i => i.ProductName).ToList();

        var cost = plan.UnitCost is { } unitCost ? decimal.Round(unitCost * plan.Quantity, 2, MidpointRounding.AwayFromZero) : (decimal?)null;
        return new PrepPlanDto(prep.Id, prep.Name, prep.Unit, plan.Quantity, plan.IsSatisfied, plan.ExpiresAt, prep.DefaultShelfLifeHours ?? 0,
            plan.LimitingProductId is { } limiting ? names[limiting].Name : null, cost, rescued, ingredients);
    }
}
