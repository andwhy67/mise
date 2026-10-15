using System.Security.Cryptography;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Mise.Application.Abstractions;
using Mise.Application.Common;
using Mise.Application.HotOffers;
using Mise.Application.Menu;
using Mise.Application.Orders;
using Mise.Domain.Menu;
using Mise.Domain.Orders;
using Mise.Domain.Restaurants;

namespace Mise.Application.GuestMenu;

/// <summary>
/// QR-меню: гость без входа видит меню ресторана по ключу из QR и заказывает. Заказ проходит те же проверки,
/// что и у повара (суммарная проверка, FEFO, 422), и сразу попадает на экран кухни с номером стола.
/// Ресторан запроса — по ключу меню: все запросы к данным идут от его имени (<see cref="ITenant.Use"/>).
/// </summary>
public sealed class GuestMenuService(
    IMiseDbContext db,
    ITenant tenant,
    MenuQueryService menu,
    HotOfferService hotOffers,
    OrderService orders,
    IValidator<PlaceGuestOrderRequest> orderValidator,
    IValidator<UpdateGuestMenuRequest> settingsValidator)
{
    /// <summary>Порций в одном заказе гостя: больше — это банкет, его принимает официант.</summary>
    public const int MaxPortions = 20;
    private const int Recommendations = 3;
    private const int TokenLength = 24;
    private const string KeyAlphabet = "abcdefghjkmnpqrstuvwxyz23456789";

    public static class Errors
    {
        public const string OrderingOff = "guest.ordering_off";
        public const string Unavailable = "guest.unavailable";
        public const string InvalidTable = "guest.invalid_table";
    }

    public async Task<GuestMenuDto> GetMenuAsync(string key, CancellationToken ct)
    {
        var restaurant = await FindAsync(key, ct);
        using var _ = tenant.Use(restaurant.Id);

        var dishes = await menu.GetAvailabilityAsync(ct);
        var recommended = (await hotOffers.GetAsync(Recommendations, null, ct)).Offers.Select(o => o.DishId).ToHashSet();
        var list = dishes
            // Выключенное вручную менеджер убрал из меню — гостю его не показываем вовсе.
            .Where(d => d.Status != DishAvailabilityStatus.StopManual)
            .Select(d => ToGuest(d, recommended.Contains(d.DishId)))
            .OrderBy(d => d.Category is null).ThenBy(d => d.Category, StringComparer.CurrentCulture).ThenBy(d => d.Name, StringComparer.CurrentCulture)
            .ToList();
        return new GuestMenuDto(restaurant.Name, restaurant.GuestOrdering, restaurant.GuestTables, MaxPortions, list);
    }

    public async Task<GuestOrderDto> PlaceOrderAsync(string key, PlaceGuestOrderRequest request, string? idempotencyKey, CancellationToken ct)
    {
        await orderValidator.EnsureValidAsync(request, ct);
        var restaurant = await FindAsync(key, ct);
        if (!restaurant.GuestOrdering)
            throw new ConflictException(Errors.OrderingOff, "Ресторан сейчас не принимает заказы через QR-меню — позовите официанта.");
        if (request.Table is { } table && table > restaurant.GuestTables)
            throw new BusinessRuleException(Errors.InvalidTable, $"Стола №{table} нет — проверьте QR-код на столе.");

        using var _ = tenant.Use(restaurant.Id);
        var origin = new GuestOrigin(request.Table, request.Payment, RandomNumberGenerator.GetString(KeyAlphabet, TokenLength));
        CreateOrderResult result;
        try
        {
            // Гостю — только изменения блюд; замены на весь заказ (как у повара после отказа) он не выбирает.
            result = await orders.CreateAsync(new CreateOrderRequest(request.Items), idempotencyKey, ct, origin);
        }
        catch (BusinessRuleException refusal) when (refusal.Code == OrderService.Errors.InsufficientStock)
        {
            // Гостю не показываем граммы на складе — только какие блюда закончились.
            var dishes = refusal.Details.GetValueOrDefault("missing") is IEnumerable<MissingIngredientDto> missing
                ? missing.SelectMany(m => m.Dishes).Distinct().OrderBy(n => n).ToList()
                : [];
            throw new BusinessRuleException(Errors.Unavailable,
                dishes.Count > 0
                    ? $"Пока вы выбирали, закончилось: {string.Join(", ", dishes)}. Уберите эти блюда или выберите изменения."
                    : "Пока вы выбирали, часть блюд закончилась.",
                new Dictionary<string, object?> { ["dishes"] = dishes });
        }

        var token = await db.Orders.AsNoTracking().Where(o => o.Id == result.Order.Id).Select(o => o.GuestToken).SingleAsync(ct);
        return await LoadOrderAsync(token!, ct);
    }

    public async Task<GuestOrderDto> GetOrderAsync(string key, string token, CancellationToken ct)
    {
        var restaurant = await FindAsync(key, ct);
        using var _ = tenant.Use(restaurant.Id);
        return await LoadOrderAsync(token, ct);
    }

    private async Task<GuestOrderDto> LoadOrderAsync(string token, CancellationToken ct)
    {
        var order = await db.Orders.AsNoTracking().Include(o => o.Items)
            .FirstOrDefaultAsync(o => o.GuestToken == token, ct) ?? throw new NotFoundException("Заказ", token);
        var full = await orders.GetAsync(order.Id, ct);
        return new GuestOrderDto(token, full.Number, full.Status, full.CreatedAt, order.TableNumber, order.Payment ?? PaymentMethod.Cash,
            full.Total, full.Items.Select(i => new GuestOrderItemDto(i.DishName, i.Quantity, i.Amount, i.Modifications)).ToList());
    }

    // ---------- Настройки (менеджер) ----------

    public async Task<GuestMenuSettingsDto> GetSettingsAsync(CancellationToken ct)
    {
        var restaurant = await CurrentAsync(ct);
        if (restaurant.MenuKey is null)
        {
            restaurant.SetMenuKey(NewKey());
            await db.SaveChangesAsync(ct);
        }
        return ToSettings(restaurant);
    }

    public async Task<GuestMenuSettingsDto> UpdateSettingsAsync(UpdateGuestMenuRequest request, CancellationToken ct)
    {
        await settingsValidator.EnsureValidAsync(request, ct);
        var restaurant = await CurrentAsync(ct);
        restaurant.ConfigureGuestMenu(request.Ordering, request.Tables);
        if (restaurant.MenuKey is null)
            restaurant.SetMenuKey(NewKey());
        await db.SaveChangesAsync(ct);
        return ToSettings(restaurant);
    }

    /// <summary>Новый ключ: старые QR перестают работать (например, их унесли или сфотографировали).</summary>
    public async Task<GuestMenuSettingsDto> RegenerateKeyAsync(CancellationToken ct)
    {
        var restaurant = await CurrentAsync(ct);
        restaurant.SetMenuKey(NewKey());
        await db.SaveChangesAsync(ct);
        return ToSettings(restaurant);
    }

    private async Task<Restaurant> CurrentAsync(CancellationToken ct)
    {
        var id = tenant.RestaurantId ?? throw new UnauthorizedException("auth.no_restaurant", "Нужен вход в ресторан.");
        return await db.Restaurants.SingleAsync(r => r.Id == id, ct);
    }

    private async Task<Restaurant> FindAsync(string key, CancellationToken ct)
    {
        var normalized = key.Trim().ToLowerInvariant();
        if (normalized.Length != Restaurant.MenuKeyLength)
            throw new NotFoundException("Меню", key);
        return await db.Restaurants.AsNoTracking().FirstOrDefaultAsync(r => r.MenuKey == normalized, ct)
            ?? throw new NotFoundException("Меню", key);
    }

    private static string NewKey() => RandomNumberGenerator.GetString(KeyAlphabet, Restaurant.MenuKeyLength);

    private static GuestMenuSettingsDto ToSettings(Restaurant r) => new(r.MenuKey!, r.GuestOrdering, r.GuestTables);

    private static GuestDishDto ToGuest(DishAvailabilityDto dish, bool recommended)
    {
        var state = dish.Status == DishAvailabilityStatus.Available
            ? GuestDishState.Available
            : dish.Alternative is not null ? GuestDishState.WithChanges : GuestDishState.Unavailable;
        var ingredients = dish.Ingredients
            .Select(i => new GuestIngredientDto(
                i.ProductId, i.ProductName, i.Removable, i.Usable < i.QtyPerPortion,
                i.Substitutes.Where(s => s.Usable >= i.QtyPerPortion * s.Ratio).Select(s => new GuestSubstituteDto(s.ProductId, s.ProductName)).ToList()))
            .ToList();
        var changes = dish.Alternative?.Changes.Select(c => new GuestChangeDto(c.ProductId, c.Kind, c.SubstituteId)).ToList() ?? [];
        return new GuestDishDto(dish.DishId, dish.DishName, dish.Category, dish.Price, state, recommended && state == GuestDishState.Available,
            ingredients, changes);
    }
}
