using FluentValidation;
using FluentValidation.Results;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Mise.Application.Abstractions;
using Mise.Application.Common;
using Mise.Application.Menu;
using Mise.Application.Realtime;
using Mise.Application.Stock;
using Mise.Domain.Abstractions;
using Mise.Domain.Catalog;
using Mise.Domain.Orders;
using Mise.Domain.Stock;

namespace Mise.Application.Orders;

/// <summary>
/// Заказы: суммарная проверка наличия, списание по FEFO под блокировкой партий, отмена с возвратом.
/// Остаток не может уйти в минус: проверка и списание идут по строкам, заблокированным SELECT … FOR UPDATE.
/// </summary>
public sealed class OrderService(
    IMiseDbContext db,
    IStockLocks locks,
    IClock clock,
    StopListService stopList,
    IRealtimeNotifier notifier,
    IValidator<CreateOrderRequest> createValidator,
    IValidator<ChangeOrderStatusRequest> statusValidator,
    IOptions<StockOptions> stockOptions,
    ILogger<OrderService> logger)
{
    public static class Errors
    {
        public const string InsufficientStock = "order.insufficient_stock";
        public const string DishUnavailable = "order.dish_unavailable";
        public const string IdempotencyKeyReused = "order.idempotency_key_reused";
        public const string NotCancellable = "order.not_cancellable";
    }

    /// <param name="guest">Заказ гостя из QR-меню: стол, способ оплаты, токен ссылки на статус. Проверки — те же.</param>
    public async Task<CreateOrderResult> CreateAsync(CreateOrderRequest request, string? idempotencyKey, CancellationToken ct, GuestOrigin? guest = null)
    {
        await createValidator.EnsureValidAsync(request, ct);
        var key = RequestHash.NormalizeKey(idempotencyKey);
        var hash = RequestHash.Compute(request);

        if (await FindReplayAsync(key, hash, ct) is { } replay)
            return new CreateOrderResult(replay, Created: false);

        var dishes = await LoadDishesAsync(request, ct);
        EnsureDishesAreOn(dishes.Values);
        var substitutions = await LoadSubstitutionsAsync(request, dishes, ct);
        var modifications = await ResolveModificationsAsync(request, ct);
        var demand = OrderDemand.Aggregate(
            request.Items.Select((i, index) => new DemandLine(dishes[i.DishId], i.Quantity, modifications[index])), substitutions);

        try
        {
            var (order, depleted) = await PlaceAsync(request, dishes, modifications, substitutions, demand, key, hash, guest, ct);
            notifier.OrderCreated(order.Id);
            notifier.StockChanged(demand.Keys);
            notifier.BatchStatusChanged(depleted);
            await stopList.TryRefreshAsync(ct);
            return new CreateOrderResult(await GetAsync(order.Id, ct), Created: true);
        }
        catch (UniqueConstraintViolationException)
        {
            // Параллельный запрос с тем же Idempotency-Key успел раньше — наша транзакция откатилась целиком.
            db.ChangeTracker.Clear();
            if (await FindReplayAsync(key, hash, ct) is { } concurrentReplay)
                return new CreateOrderResult(concurrentReplay, Created: false);
            throw;
        }
    }

    /// <returns>Заказ и партии, израсходованные им до нуля (сменили статус на Depleted).</returns>
    private async Task<(Order Order, IReadOnlyList<Guid> Depleted)> PlaceAsync(
        CreateOrderRequest request,
        IReadOnlyDictionary<Guid, Dish> dishes,
        IReadOnlyList<IReadOnlyList<ItemModification>> modifications,
        IReadOnlyList<Substitution> substitutions,
        IReadOnlyDictionary<Guid, decimal> demand,
        string? key,
        string hash,
        GuestOrigin? guest,
        CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var now = clock.UtcNow;

        // Блокируем все пригодные партии нужных продуктов (детерминированный порядок → без дедлоков).
        var batches = (await locks.LockUsableBatchesAsync(demand.Keys.ToList(), now, ct)).ToLookup(b => b.ProductId);

        var plan = demand.ToDictionary(d => d.Key, d => FefoAllocator.Allocate(d.Value, batches[d.Key], now));
        var shortages = plan.Where(p => !p.Value.IsSatisfied).ToList();
        if (shortages.Count > 0)
        {
            var refusal = await InsufficientStockAsync(request, dishes, substitutions, demand, shortages.Select(s => (s.Key, s.Value)), now, ct);
            // Ничего не списываем (правило 4), блокировки снимаем сразу; факт отказа — для отчёта «чего не хватает».
            await transaction.RollbackAsync(ct);
            await RecordShortagesAsync(shortages.Select(s => (s.Key, demand[s.Key], s.Value.Available)), now, ct);
            throw refusal;
        }

        var number = await locks.NextOrderNumberAsync(ct);
        var order = new Order(Guid.CreateVersion7(now), $"ORD-{number:D6}", now) { IdempotencyKey = key, RequestHash = hash };
        if (guest is not null)
            order.MarkGuest(guest.TableNumber, guest.Payment, guest.Token);
        foreach (var (item, index) in request.Items.Select((item, index) => (item, index)))
        {
            var dish = dishes[item.DishId];
            order.AddItem(dish.Id, dish.Name, item.Quantity, dish.Price, modifications[index]);
        }
        foreach (var substitution in substitutions)
            order.AddSubstitution(substitution);

        var batchById = batches.SelectMany(g => g).ToDictionary(b => b.Id);
        var depleted = new List<Guid>();
        foreach (var allocation in plan.Values.SelectMany(p => p.Allocations))
        {
            var batch = batchById[allocation.BatchId];
            batch.Consume(allocation.Quantity, now);
            db.StockMovements.Add(StockMovement.OrderConsumption(Guid.CreateVersion7(now), batch, allocation.Quantity, order.Id, now, stockOptions.Value.Rescue));
            if (batch.Status == BatchStatus.Depleted)
                depleted.Add(batch.Id);
        }

        db.Orders.Add(order);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return (order, depleted);
    }

    public async Task<CancelOrderResult> CancelAsync(Guid id, CancellationToken ct)
    {
        var returned = new List<CancelledLineDto>();
        var lost = new List<CancelledLineDto>();
        var reactivated = new List<Guid>();
        await using (var transaction = await db.Database.BeginTransactionAsync(ct))
        {
            var now = clock.UtcNow;
            var order = await locks.LockOrderAsync(id, ct) ?? throw new NotFoundException("Заказ", id);
            if (!order.CanBeCancelled)
                throw new ConflictException(Errors.NotCancellable, $"Заказ {order.Number} в статусе {order.Status} отменить нельзя.");

            var consumed = await db.StockMovements.AsNoTracking()
                .Where(m => m.OrderId == id && m.Type == MovementType.OrderConsumption)
                .GroupBy(m => m.BatchId)
                .Select(g => new { BatchId = g.Key, Qty = -g.Sum(m => m.QtyDelta) })
                .ToListAsync(ct);
            var batches = (await locks.LockBatchesAsync(consumed.Select(c => c.BatchId).ToList(), ct)).ToDictionary(b => b.Id);
            var products = await LoadProductNamesAsync(batches.Values.Select(b => b.ProductId), ct);

            foreach (var line in consumed.Where(c => c.Qty > 0))
            {
                var batch = batches[line.BatchId];
                var (name, unit) = products[batch.ProductId];
                var dto = new CancelledLineDto(batch.ProductId, name, unit, batch.Id, line.Qty);
                if (batch.CanAcceptReturnAt(now))
                {
                    if (batch.Status == BatchStatus.Depleted)
                        reactivated.Add(batch.Id);
                    batch.Return(line.Qty, now);
                    db.StockMovements.Add(StockMovement.OrderCancelReturn(Guid.CreateVersion7(now), batch, line.Qty, id, now));
                    returned.Add(dto);
                }
                else
                {
                    // Партия уже просрочена или списана — продукт в неё не возвращается (PLAN §4.2).
                    lost.Add(dto);
                }
            }

            order.Cancel(now);
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }

        notifier.OrderStatusChanged(id);
        notifier.StockChanged(returned.Select(r => r.ProductId).Distinct());
        notifier.BatchStatusChanged(reactivated);
        await stopList.TryRefreshAsync(ct);
        return new CancelOrderResult(await GetAsync(id, ct), returned, lost);
    }

    public async Task<OrderDto> ChangeStatusAsync(Guid id, ChangeOrderStatusRequest request, CancellationToken ct)
    {
        await statusValidator.EnsureValidAsync(request, ct);
        await using (var transaction = await db.Database.BeginTransactionAsync(ct))
        {
            var order = await locks.LockOrderAsync(id, ct) ?? throw new NotFoundException("Заказ", id);
            order.ChangeStatus(request.Status, clock.UtcNow);
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }
        notifier.OrderStatusChanged(id);
        return await GetAsync(id, ct);
    }

    public async Task<OrderDto> GetAsync(Guid id, CancellationToken ct)
    {
        var order = await db.Orders.AsNoTracking().Include(o => o.Items).Include(o => o.Substitutions).FirstOrDefaultAsync(o => o.Id == id, ct)
            ?? throw new NotFoundException("Заказ", id);

        var movements = await (
                from m in db.StockMovements.AsNoTracking()
                where m.OrderId == id
                join b in db.Batches on m.BatchId equals b.Id
                join p in db.Products on m.ProductId equals p.Id
                select new { m.ProductId, p.Name, p.Unit, m.BatchId, b.ExpiresAt, m.Type, m.QtyDelta })
            .ToListAsync(ct);

        var consumption = movements
            .GroupBy(m => new { m.ProductId, m.Name, m.Unit, m.BatchId, m.ExpiresAt })
            .Select(g => new OrderConsumptionDto(
                g.Key.ProductId, g.Key.Name, g.Key.Unit, g.Key.BatchId, g.Key.ExpiresAt,
                -g.Where(m => m.Type == MovementType.OrderConsumption).Sum(m => m.QtyDelta),
                g.Where(m => m.Type == MovementType.OrderCancelReturn).Sum(m => m.QtyDelta)))
            .OrderBy(c => c.ProductName).ThenBy(c => c.BatchExpiresAt)
            .ToList();

        var names = await LoadProductNamesAsync(order.Items.SelectMany(ModifiedProducts), ct);
        return new OrderDto(
            order.Id, order.Number, order.Status, order.CreatedAt, order.UpdatedAt, order.CancelledAt, order.Total,
            order.Items.OrderBy(i => i.LineNo)
                .Select(i => new OrderItemDto(i.LineNo, i.DishId, i.DishName, i.Quantity, i.PriceSnapshot, i.PriceSnapshot * i.Quantity,
                    ModificationDtos(i.Modifications, names)))
                .ToList(),
            consumption,
            await SubstitutionDtosAsync(order.Substitutions, ct));
    }

    private async Task<IReadOnlyList<OrderSubstitutionDto>> SubstitutionDtosAsync(IReadOnlyCollection<OrderSubstitution> substitutions, CancellationToken ct)
    {
        if (substitutions.Count == 0)
            return [];
        var names = await LoadProductNamesAsync(substitutions.SelectMany(s => new[] { s.ProductId, s.SubstituteId }), ct);
        return substitutions
            .Select(s => new OrderSubstitutionDto(s.ProductId, names[s.ProductId].Name, s.SubstituteId, names[s.SubstituteId].Name,
                names[s.SubstituteId].Unit, s.Ratio))
            .OrderBy(s => s.ProductName)
            .ToList();
    }

    private static IEnumerable<Guid> ModifiedProducts(OrderItem item) =>
        item.Modifications.SelectMany(m => m.SubstituteId is { } s ? new[] { m.ProductId, s } : [m.ProductId]);

    private static IReadOnlyList<OrderItemModificationDto> ModificationDtos(
        IReadOnlyCollection<OrderItemModification> modifications, IReadOnlyDictionary<Guid, (string Name, Domain.Common.MeasureUnit Unit)> names) =>
        modifications.Select(m => new OrderItemModificationDto(m.ProductId, names.GetValueOrDefault(m.ProductId).Name ?? "?", m.Kind, m.SubstituteId,
            m.SubstituteId is { } s ? names.GetValueOrDefault(s).Name ?? "?" : null)).ToList();

    /// <summary>
    /// Изменения строк заказа: «убрать» — домен проверит, что ингредиент «можно убрать»; «заменить» — только по правилу
    /// справочника, коэффициент берётся оттуда (клиент его не задаёт).
    /// </summary>
    private async Task<IReadOnlyList<IReadOnlyList<ItemModification>>> ResolveModificationsAsync(CreateOrderRequest request, CancellationToken ct)
    {
        var replaced = request.Items.SelectMany(i => i.Modifications ?? []).Where(m => m.Kind == ModificationKind.Replace)
            .Select(m => m.ProductId).Distinct().ToList();
        var rules = replaced.Count == 0
            ? []
            : await db.Products.AsNoTracking().Where(p => replaced.Contains(p.Id)).SelectMany(p => p.Substitutes).ToListAsync(ct);

        var failures = new List<ValidationFailure>();
        var result = request.Items.Select((item, index) => (IReadOnlyList<ItemModification>)(item.Modifications ?? []).Select((m, mi) =>
        {
            if (m.Kind == ModificationKind.Remove)
                return new ItemModification(m.ProductId, ModificationKind.Remove);
            var rule = rules.FirstOrDefault(r => r.ProductId == m.ProductId && r.SubstituteId == m.SubstituteId);
            if (rule is null)
                failures.Add(new ValidationFailure($"Items[{index}].Modifications[{mi}].SubstituteId", "Такой замены нет в справочнике продуктов."));
            return new ItemModification(m.ProductId, ModificationKind.Replace, m.SubstituteId, rule?.Ratio ?? 1m);
        }).ToList()).ToList();
        if (failures.Count > 0)
            throw failures.ToValidationException();
        return result;
    }

    /// <summary>
    /// Замены из запроса — только по правилам справочника и только для продуктов, которые есть в блюдах заказа.
    /// Коэффициент берётся из справочника на момент заказа и сохраняется в нём.
    /// </summary>
    private async Task<IReadOnlyList<Substitution>> LoadSubstitutionsAsync(CreateOrderRequest request, IReadOnlyDictionary<Guid, Dish> dishes, CancellationToken ct)
    {
        if (request.Substitutions is not { Count: > 0 } requested)
            return [];
        var productIds = requested.Select(s => s.ProductId).ToList();
        var rules = await db.Products.AsNoTracking()
            .Where(p => productIds.Contains(p.Id))
            .SelectMany(p => p.Substitutes)
            .ToListAsync(ct);
        var used = request.Items.SelectMany(i => dishes[i.DishId].Ingredients).Select(i => i.ProductId).ToHashSet();

        var failures = new List<ValidationFailure>();
        var result = new List<Substitution>();
        foreach (var (s, index) in requested.Select((s, index) => (s, index)))
        {
            var rule = rules.FirstOrDefault(r => r.ProductId == s.ProductId && r.SubstituteId == s.SubstituteId);
            if (!used.Contains(s.ProductId))
                failures.Add(new ValidationFailure($"Substitutions[{index}].ProductId", "Этого продукта нет в блюдах заказа."));
            else if (rule is null)
                failures.Add(new ValidationFailure($"Substitutions[{index}].SubstituteId", "Такой замены нет в справочнике продуктов."));
            else
                result.Add(new Substitution(rule.ProductId, rule.SubstituteId, rule.Ratio));
        }
        if (failures.Count > 0)
            throw failures.ToValidationException();
        return result;
    }

    /// <summary>Максимум карточек на экране кухни — больше заказов в работе одновременно не бывает (и не уместится).</summary>
    public const int KitchenLimit = 100;

    /// <summary>
    /// Экран кухни: заказы в работе (Принят, Готовится, Готов) с составом — одним запросом, старые сверху.
    /// UpdatedAt меняется только при смене статуса, поэтому это и есть «с какого момента в этой колонке».
    /// </summary>
    public async Task<IReadOnlyList<KitchenOrderDto>> GetKitchenAsync(CancellationToken ct)
    {
        var orders = await db.Orders.AsNoTracking()
            .Where(o => o.Status == OrderStatus.Created || o.Status == OrderStatus.Cooking || o.Status == OrderStatus.Done)
            // При переполнении отрезаем самые старые, а не новые: новый заказ на кухне важнее зависшего.
            .OrderByDescending(o => o.CreatedAt).ThenByDescending(o => o.Id)
            .Take(KitchenLimit)
            .Include(o => o.Items)
            .Include(o => o.Substitutions)
            .ToListAsync(ct);
        orders.Reverse();
        var productIds = orders.SelectMany(o => o.Substitutions).SelectMany(s => new[] { s.ProductId, s.SubstituteId })
            .Concat(orders.SelectMany(o => o.Items).SelectMany(ModifiedProducts)).Distinct().ToList();
        var names = productIds.Count == 0 ? [] : await LoadProductNamesAsync(productIds, ct);
        return orders.Select(o => new KitchenOrderDto(
                o.Id, o.Number, o.Status, o.CreatedAt, o.UpdatedAt, o.Items.Sum(i => i.Quantity),
                o.Items.OrderBy(i => i.LineNo).Select(i => new KitchenItemDto(i.DishName, i.Quantity, ModificationDtos(i.Modifications, names))).ToList(),
                o.Substitutions.Select(s => new KitchenSubstitutionDto(names.GetValueOrDefault(s.ProductId).Name ?? "?", names.GetValueOrDefault(s.SubstituteId).Name ?? "?")).ToList(),
                o.Source, o.TableNumber, o.Payment))
            .ToList();
    }

    public async Task<PagedResult<OrderListItemDto>> ListAsync(OrderStatus? status, PageRequest page, CancellationToken ct)
    {
        var query = db.Orders.AsNoTracking();
        if (status is { } s)
            query = query.Where(o => o.Status == s);

        var total = await query.CountAsync(ct);
        var items = await ToListItems(query
                .OrderByDescending(o => o.CreatedAt).ThenByDescending(o => o.Id)
                .Skip(page.Skip).Take(page.SafePageSize))
            .ToListAsync(ct);
        return new PagedResult<OrderListItemDto>(items, total, page.SafePage, page.SafePageSize);
    }

    public async Task<IReadOnlyList<OrderListItemDto>> GetListItemsAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct) =>
        ids.Count == 0
            ? []
            : await ToListItems(db.Orders.AsNoTracking().Where(o => ids.Contains(o.Id)).OrderBy(o => o.CreatedAt).ThenBy(o => o.Id))
                .ToListAsync(ct);

    private static IQueryable<OrderListItemDto> ToListItems(IQueryable<Order> orders) =>
        orders.Select(o => new OrderListItemDto(
            o.Id, o.Number, o.Status, o.CreatedAt,
            o.Items.Sum(i => i.Quantity),
            o.Items.Sum(i => i.PriceSnapshot * i.Quantity),
            o.Source, o.TableNumber));

    private async Task<Dictionary<Guid, Dish>> LoadDishesAsync(CreateOrderRequest request, CancellationToken ct)
    {
        var ids = request.Items.Select(i => i.DishId).Distinct().ToList();
        var dishes = await db.Dishes.AsNoTracking().Include(d => d.Ingredients).Where(d => ids.Contains(d.Id)).ToDictionaryAsync(d => d.Id, ct);

        var failures = request.Items
            .Select((item, index) => (item, index))
            .Where(x => !dishes.ContainsKey(x.item.DishId))
            .Select(x => new ValidationFailure($"Items[{x.index}].DishId", $"Блюдо «{x.item.DishId}» не найдено в меню."))
            .ToList();
        if (failures.Count > 0)
            throw failures.ToValidationException();
        return dishes;
    }

    private static void EnsureDishesAreOn(IEnumerable<Dish> dishes)
    {
        var off = dishes.Where(d => !d.IsActive).Select(d => d.Name).OrderBy(n => n).ToList();
        if (off.Count > 0)
            throw new BusinessRuleException(Errors.DishUnavailable,
                $"Блюда выключены из меню вручную: {string.Join(", ", off)}.",
                new Dictionary<string, object?> { ["dishes"] = off });
    }

    /// <summary>Запись отказа не должна подменять ответ 422: при сбое — только лог (через исключение выше не пробрасываем).</summary>
    private async Task RecordShortagesAsync(IEnumerable<(Guid ProductId, decimal Required, decimal Available)> shortages, DateTimeOffset now, CancellationToken ct)
    {
        try
        {
            db.ChangeTracker.Clear();
            foreach (var (productId, required, available) in shortages)
                db.OrderShortages.Add(new OrderShortage(Guid.CreateVersion7(now), productId, required, available, now));
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Не удалось записать отказ заказа для аналитики дефицита");
        }
    }

    private async Task<BusinessRuleException> InsufficientStockAsync(
        CreateOrderRequest request,
        IReadOnlyDictionary<Guid, Dish> dishes,
        IReadOnlyList<Substitution> substitutions,
        IReadOnlyDictionary<Guid, decimal> demand,
        IEnumerable<(Guid ProductId, AllocationResult Result)> shortages,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var list = shortages.ToList();
        var shortIds = list.Select(s => s.ProductId).ToList();
        // Заменители для недостающих продуктов — правила справочника; уже использованные в заказе как замена не предлагаем.
        var rules = await db.Products.AsNoTracking()
            .Where(p => shortIds.Contains(p.Id))
            .SelectMany(p => p.Substitutes)
            .ToListAsync(ct);
        var replaced = substitutions.Select(s => s.ProductId).ToHashSet();
        rules = rules.Where(r => !replaced.Contains(r.SubstituteId) && !shortIds.Contains(r.SubstituteId)).ToList();
        var usable = await MenuSnapshot.LoadUsableAsync(db, now, ct);
        var products = await LoadProductNamesAsync(shortIds.Concat(rules.Select(r => r.SubstituteId)), ct);
        var orderDishes = request.Items.Select(i => dishes[i.DishId]).DistinctBy(d => d.Id).ToList();
        // Продукт, вместо которого уже взята замена, — в составе блюд под исходным id.
        var originals = substitutions.ToLookup(s => s.SubstituteId, s => s.ProductId);

        var missing = list
            .Select(s =>
            {
                var (name, unit) = products[s.ProductId];
                var usedBy = orderDishes
                    .Where(d => d.Ingredients.Any(i => i.ProductId == s.ProductId || originals[s.ProductId].Contains(i.ProductId)))
                    .Select(d => d.Name).ToList();
                var offers = rules.Where(r => r.ProductId == s.ProductId)
                    .Select(r =>
                    {
                        var (subName, subUnit) = products[r.SubstituteId];
                        var required = demand[s.ProductId] * r.Ratio + demand.GetValueOrDefault(r.SubstituteId);
                        var available = usable.GetValueOrDefault(r.SubstituteId);
                        return new SubstituteOfferDto(r.SubstituteId, subName, subUnit, r.Ratio, required, available, available >= required);
                    })
                    .OrderByDescending(o => o.Enough).ThenBy(o => o.ProductName)
                    .ToList();
                return new MissingIngredientDto(s.ProductId, name, unit, demand[s.ProductId], s.Result.Available, s.Result.Shortage, usedBy, offers);
            })
            .OrderBy(m => m.ProductName)
            .ToList();

        var summary = string.Join(", ", missing.Select(m => $"{m.ProductName}: нужно {m.Required:0.###}, доступно {m.Available:0.###}"));
        return new BusinessRuleException(Errors.InsufficientStock, $"Недостаточно ингредиентов — {summary}.",
            new Dictionary<string, object?> { ["missing"] = missing });
    }

    private async Task<Dictionary<Guid, (string Name, Domain.Common.MeasureUnit Unit)>> LoadProductNamesAsync(IEnumerable<Guid> ids, CancellationToken ct)
    {
        var list = ids.Distinct().ToList();
        return await db.Products.AsNoTracking()
            .Where(p => list.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, p => (p.Name, p.Unit), ct);
    }

    private async Task<OrderDto?> FindReplayAsync(string? key, string hash, CancellationToken ct)
    {
        if (key is null)
            return null;
        var existing = await db.Orders.AsNoTracking()
            .Where(o => o.IdempotencyKey == key)
            .Select(o => new { o.Id, o.RequestHash })
            .FirstOrDefaultAsync(ct);
        if (existing is null)
            return null;
        if (existing.RequestHash != hash)
            throw new ConflictException(Errors.IdempotencyKeyReused, "Idempotency-Key уже использован для другого заказа.");
        return await GetAsync(existing.Id, ct);
    }
}
