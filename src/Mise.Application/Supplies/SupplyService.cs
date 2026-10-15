using FluentValidation;
using FluentValidation.Results;
using Microsoft.EntityFrameworkCore;
using Mise.Application.Abstractions;
using Mise.Application.Common;
using Mise.Application.Menu;
using Mise.Application.Realtime;
using Mise.Domain.Abstractions;
using Mise.Domain.Catalog;
using Mise.Domain.Common;
using Mise.Domain.Expiry;
using Mise.Domain.Stock;
using Mise.Domain.Supplies;

namespace Mise.Application.Supplies;

public sealed class SupplyService(
    IMiseDbContext db,
    IClock clock,
    StopListService stopList,
    IRealtimeNotifier notifier,
    IValidator<CreateSupplyRequest> validator)
{
    /// <summary>Допустимое «опережение» даты поставки относительно часов сервера (assumptions A3).</summary>
    public static readonly TimeSpan MaxClockSkew = TimeSpan.FromMinutes(5);

    public static class Errors
    {
        public const string IdempotencyKeyReused = "supply.idempotency_key_reused";
        public const string DuplicateNumber = "supply.duplicate_number";
        public const string DuplicateId = "supply.duplicate_id";
    }

    /// <summary>
    /// Принимает поставку: каждая позиция → отдельная партия со своим expires_at + движение Receipt.
    /// Всё сохраняется одним SaveChanges (одна транзакция).
    /// </summary>
    public async Task<CreateSupplyResult> CreateAsync(CreateSupplyRequest request, string? idempotencyKey, CancellationToken ct)
    {
        await validator.EnsureValidAsync(request, ct);
        var key = RequestHash.NormalizeKey(idempotencyKey);
        var hash = RequestHash.Compute(request);

        if (await FindReplayAsync(request, key, hash, ct) is { } replay)
            return new CreateSupplyResult(replay, Created: false);

        var now = clock.UtcNow;
        var createdAt = (request.CreatedAt ?? now).ToUniversalTime();
        if (createdAt > now + MaxClockSkew)
            throw new[] { new ValidationFailure(nameof(request.CreatedAt), "Дата создания поставки не может быть в будущем.") }
                .ToValidationException();

        var productIds = request.Items.Select(i => i.ProductId).Distinct().ToList();
        var products = await db.Products.AsNoTracking().Where(p => productIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, ct);

        var supply = new Supply(request.Id ?? Guid.CreateVersion7(now), request.Number.Trim(), createdAt, now)
        {
            SupplierName = string.IsNullOrWhiteSpace(request.Supplier) ? null : request.Supplier.Trim(),
            IdempotencyKey = key,
            RequestHash = hash,
        };

        var failures = new List<ValidationFailure>();
        var batches = new List<Batch>();
        for (var index = 0; index < request.Items.Count; index++)
        {
            var item = request.Items[index];
            var path = $"Items[{index}]";
            if (!products.TryGetValue(item.ProductId, out var product))
            {
                failures.Add(new ValidationFailure($"{path}.ProductId", $"Продукт «{item.ProductId}» не найден в словаре."));
                continue;
            }

            try
            {
                batches.Add(RegisterItem(supply, index + 1, item, product, createdAt, now));
            }
            catch (DomainException ex)
            {
                var property = ex.Code.StartsWith("unit.", StringComparison.Ordinal) ? "Quantity" : "ExpiresAt";
                failures.Add(new ValidationFailure($"{path}.{property}", ex.Message) { ErrorCode = ex.Code });
            }
        }

        if (failures.Count > 0)
            throw failures.ToValidationException();

        db.Supplies.Add(supply);
        db.Batches.AddRange(batches);
        db.StockMovements.AddRange(batches.Select(b => StockMovement.Receipt(Guid.CreateVersion7(now), b, supply.Id, now)));

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (UniqueConstraintViolationException)
        {
            // Параллельный запрос успел раньше: повторяем проверки уже по сохранённым данным.
            db.ChangeTracker.Clear();
            if (await FindReplayAsync(request, key, hash, ct) is { } concurrentReplay)
                return new CreateSupplyResult(concurrentReplay, Created: false);
            throw new ConflictException(Errors.DuplicateNumber, $"Поставка с номером «{supply.Number}» уже зарегистрирована.");
        }

        notifier.SupplyReceived(supply.Id);
        notifier.StockChanged(productIds);
        await stopList.TryRefreshAsync(ct);
        return new CreateSupplyResult(ToDto(supply, batches, products), Created: true);
    }

    public async Task<SupplyDto> GetAsync(Guid id, CancellationToken ct) =>
        await LoadDtoAsync(id, ct) ?? throw new NotFoundException("Поставка", id);

    public async Task<PagedResult<SupplyListItemDto>> ListAsync(PageRequest page, CancellationToken ct)
    {
        var query = db.Supplies.AsNoTracking();
        var total = await query.CountAsync(ct);
        var items = await query
            .OrderByDescending(s => s.CreatedAt).ThenByDescending(s => s.Id)
            .Skip(page.Skip).Take(page.SafePageSize)
            .Select(s => new SupplyListItemDto(s.Id, s.Number, s.CreatedAt, s.RegisteredAt, s.SupplierName, s.Items.Count))
            .ToListAsync(ct);
        return new PagedResult<SupplyListItemDto>(items, total, page.SafePage, page.SafePageSize);
    }

    public async Task<IReadOnlyList<SupplyListItemDto>> GetListItemsAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct) =>
        ids.Count == 0
            ? []
            : await db.Supplies.AsNoTracking()
                .Where(s => ids.Contains(s.Id))
                .OrderBy(s => s.RegisteredAt).ThenBy(s => s.Id)
                .Select(s => new SupplyListItemDto(s.Id, s.Number, s.CreatedAt, s.RegisteredAt, s.SupplierName, s.Items.Count))
                .ToListAsync(ct);

    private static Batch RegisterItem(Supply supply, int lineNo, CreateSupplyItemRequest item, Product product, DateTimeOffset createdAt, DateTimeOffset now)
    {
        var quantity = UnitConverter.ToBase(item.Quantity, item.Unit ?? UnitConverter.DefaultInputFor(product.Unit), product.Unit);
        var expiry = ExpiryPolicy.Resolve(createdAt, item.ExpiresAt, item.ShelfLifeHours, product.DefaultShelfLifeHours);
        var name = string.IsNullOrWhiteSpace(item.Name) ? product.Name : item.Name.Trim();

        // Цена в строке — за единицу ввода (₽ за кг, если вводили в кг); в партии — за базовую. Нет цены — ориентировочная из справочника.
        var unitCost = item.Price is { } price
            ? price / UnitConverter.ToBase(1m, item.Unit ?? UnitConverter.DefaultInputFor(product.Unit), product.Unit)
            : product.ReferenceUnitCost;
        var supplyItem = new SupplyItem(Guid.CreateVersion7(now), supply.Id, lineNo, product.Id, name, quantity)
        {
            ShelfLifeHours = item.ShelfLifeHours,
            ExpiresAt = item.ExpiresAt?.ToUniversalTime(),
            UnitCost = unitCost,
        };
        supply.AddItem(supplyItem);
        return Batch.Receive(Guid.CreateVersion7(now), supplyItem.Id, product.Id, createdAt, expiry, quantity, now, unitCost);
    }

    /// <summary>
    /// Идемпотентность: тот же ключ и то же тело → прежний результат; тот же ключ с другим телом → 409.
    /// Без ключа (или с новым ключом) повтор номера или id поставки → 409.
    /// </summary>
    private async Task<SupplyDto?> FindReplayAsync(CreateSupplyRequest request, string? key, string hash, CancellationToken ct)
    {
        // Один запрос по всем трём признакам: иначе параллельный повтор, закоммиченный между проверками
        // «по ключу» и «по номеру», выглядел бы как конфликт номера, а не как повтор.
        var number = request.Number.Trim();
        var requestId = request.Id;
        var matches = await db.Supplies.AsNoTracking()
            .Where(s => (key != null && s.IdempotencyKey == key) || s.Number == number || (requestId != null && s.Id == requestId))
            .Select(s => new { s.Id, s.Number, s.IdempotencyKey, s.RequestHash })
            .ToListAsync(ct);

        if (key is not null && matches.FirstOrDefault(s => s.IdempotencyKey == key) is { } byKey)
        {
            if (byKey.RequestHash != hash)
                throw new ConflictException(Errors.IdempotencyKeyReused, "Idempotency-Key уже использован для другой поставки.");
            return await LoadDtoAsync(byKey.Id, ct);
        }

        if (matches.Any(s => s.Number == number))
            throw new ConflictException(Errors.DuplicateNumber, $"Поставка с номером «{number}» уже зарегистрирована.");
        if (requestId is { } id && matches.Any(s => s.Id == id))
            throw new ConflictException(Errors.DuplicateId, $"Поставка с идентификатором «{id}» уже зарегистрирована.");
        return null;
    }

    private async Task<SupplyDto?> LoadDtoAsync(Guid id, CancellationToken ct)
    {
        var supply = await db.Supplies.AsNoTracking().Include(s => s.Items).FirstOrDefaultAsync(s => s.Id == id, ct);
        if (supply is null)
            return null;

        var itemIds = supply.Items.Select(i => i.Id).ToList();
        var batches = await db.Batches.AsNoTracking().Where(b => itemIds.Contains(b.SupplyItemId)).ToListAsync(ct);
        var productIds = supply.Items.Select(i => i.ProductId).Distinct().ToList();
        var products = await db.Products.AsNoTracking().Where(p => productIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, ct);
        return ToDto(supply, batches, products);
    }

    private static SupplyDto ToDto(Supply supply, IReadOnlyCollection<Batch> batches, IReadOnlyDictionary<Guid, Product> products)
    {
        var batchByItem = batches.ToDictionary(b => b.SupplyItemId);
        return new SupplyDto(
            supply.Id,
            supply.Number,
            supply.CreatedAt,
            supply.RegisteredAt,
            supply.SupplierName,
            supply.Items
                .OrderBy(i => i.LineNo)
                .Select(i =>
                {
                    var batch = batchByItem[i.Id];
                    var unit = products[i.ProductId].Unit;
                    return new SupplyItemDto(
                        i.Id, i.LineNo, i.ProductId, i.ProductName, unit, i.Quantity,
                        i.ShelfLifeHours, i.ExpiresAt, batch.Id, batch.ExpiresAt, batch.ExpirySource, batch.Status,
                        i.UnitCost is { } cost ? Prices.ToDisplay(cost, unit) : null,
                        batch.CostOf(i.Quantity));
                })
                .ToList());
    }
}
