using System.Globalization;
using Mise.Application.Alerts;
using Mise.Application.Common;
using Mise.Domain.Alerts;
using Mise.Domain.Common;
using Mise.Domain.Users;

namespace Mise.Application.Push;

/// <summary>Текст пуша (на языке устройства) и куда ведёт клик.</summary>
public sealed record PushMessage(string Title, string Body, string Url, string Tag, AlertSeverity Severity, string Language);

/// <summary>Кому уходит пуш по алерту: только тем, кому нужно действовать. Остальное видно в приложении.</summary>
public static class PushAudience
{
    private static readonly UserRole[] Kitchen = [UserRole.Chef, UserRole.Manager];
    private static readonly UserRole[] Stock = [UserRole.Warehouse, UserRole.Manager];
    private static readonly UserRole[] Everyone = [UserRole.Warehouse, UserRole.Chef, UserRole.Manager];

    public static IReadOnlyList<UserRole> Roles(AlertDto alert) => alert.Type switch
    {
        // «Скоро испортится» — пушем только критичное: остальное не повод отвлекать посреди смены.
        AlertType.ExpiringSoon when alert.Severity == AlertSeverity.Critical => Everyone,
        AlertType.Expired => Stock,
        AlertType.LowStock => Stock,
        AlertType.StopListOn or AlertType.StopListOff => Kitchen,
        _ => [],
    };
}

/// <summary>
/// Тексты пушей. Интерфейс собирает тексты сам (DESIGN.md §9a), но пуш показывает система, когда приложение закрыто, —
/// поэтому здесь сервер пишет готовую фразу на языке подписки (ru/en).
/// </summary>
public static class PushTexts
{
    private static readonly CultureInfo Ru = CultureInfo.GetCultureInfo("ru-RU");
    private static readonly CultureInfo En = CultureInfo.GetCultureInfo("en-US");

    public static PushMessage? Build(AlertDto alert, string language, DateTimeOffset now)
    {
        var en = language == "en";
        string? product = alert.ProductName, dish = alert.DishName;
        string? qty = alert is { Quantity: { } q, Unit: { } u } ? Quantity(q, u, en) : null;

        (string Title, string Body, string Url)? text = alert.Type switch
        {
            AlertType.ExpiringSoon when product is not null && alert.ExpiresAt is { } at => (
                en ? $"Spoiling soon: {product}" : $"Скоро испортится: {product}",
                Join(qty, When(at, now, en)) + (en ? ". Use it or write it off." : ". Пустите в работу или спишите."),
                "/stock?show=expiring"),
            AlertType.Expired when product is not null => (
                en ? $"Spoiled: {product}" : $"Испортилось: {product}",
                alert.AutoWrittenOff == true
                    ? (qty is null ? (en ? "Written off automatically." : "Списано автоматически.") : (en ? $"{qty} written off automatically." : $"{qty} списано автоматически."))
                    : (qty is null ? (en ? "Write it off." : "Нужно списать.") : (en ? $"{qty} — write it off." : $"{qty} — нужно списать.")),
                "/stock?show=spoiled"),
            AlertType.LowStock when product is not null && qty is not null => (
                en ? $"Running low: {product}" : $"Мало: {product}",
                alert is { LimitQty: { } limit, Unit: { } unit }
                    ? (en ? $"{qty} left, minimum is {Quantity(limit, unit, en)}. Time to order." : $"Осталось {qty} при норме {Quantity(limit, unit, en)}. Пора заказать.")
                    : (en ? $"{qty} left. Time to order." : $"Осталось {qty}. Пора заказать."),
                "/supplies"),
            AlertType.StopListOn when dish is not null => (
                en ? $"Can't be ordered: {dish}" : $"Нельзя заказать: {dish}",
                product is null ? (en ? "Taken off the menu by hand." : "Убрано из меню вручную.") : (en ? $"Out of {product}." : $"Закончилось: {product}."),
                "/stoplist"),
            AlertType.StopListOff when dish is not null => (
                en ? $"Back on the menu: {dish}" : $"Снова в меню: {dish}",
                en ? "The dish can be ordered again." : "Блюдо снова можно заказывать.",
                "/stoplist"),
            _ => null,
        };
        if (text is not { } t)
            return null;

        // Одна тема — одно уведомление: новое о том же продукте или блюде заменяет прежнее, а не копится.
        var subject = alert.DishId ?? alert.ProductId ?? alert.Id;
        var tag = $"{alert.Type.ToString().ToLowerInvariant()}:{subject:N}";
        return new PushMessage(t.Title, t.Body, t.Url, tag, alert.Severity, en ? "en" : "ru");
    }

    private static string Join(string? qty, string when) => qty is null ? Capitalize(when) : $"{qty} — {when}";

    /// <summary>«сегодня в 23:00», «завтра в 08:00», «17 октября в 10:00» — по Москве.</summary>
    private static string When(DateTimeOffset at, DateTimeOffset now, bool en)
    {
        var local = at.ToOffset(MoscowTime.Offset);
        var days = MoscowTime.DateOf(at).DayNumber - MoscowTime.DateOf(now).DayNumber;
        var time = local.ToString("HH:mm", CultureInfo.InvariantCulture);
        return days switch
        {
            <= 0 => en ? $"today at {time}" : $"сегодня в {time}",
            1 => en ? $"tomorrow at {time}" : $"завтра в {time}",
            _ => en ? $"{local.ToString("MMMM d", En)} at {time}" : $"{local.ToString("d MMMM", Ru)} в {time}",
        };
    }

    private static string Quantity(decimal value, MeasureUnit unit, bool en)
    {
        if (!en)
            return RuFormat.Quantity(value, unit);
        var big = unit != MeasureUnit.Piece && Math.Abs(value) >= 1000;
        var shown = decimal.Round(big ? value / 1000 : value, big || Math.Abs(value) < 100 ? 1 : 0, MidpointRounding.AwayFromZero).ToString("0.#", En);
        return unit switch
        {
            MeasureUnit.Gram => big ? $"{shown} kg" : $"{shown} g",
            MeasureUnit.Milliliter => big ? $"{shown} L" : $"{shown} ml",
            _ => $"{shown} pcs",
        };
    }

    private static string Capitalize(string text) => text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];
}
