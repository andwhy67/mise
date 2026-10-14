using Mise.Application.Alerts;
using Mise.Application.Push;
using Mise.Domain.Alerts;
using Mise.Domain.Common;
using Mise.Domain.Users;

namespace Mise.Application.Tests;

/// <summary>Пуши: кому уходит какое уведомление и что в нём написано (на языке устройства).</summary>
public sealed class PushTextsTests
{
    // Среда, 14 октября 2026, 14:00 по Москве.
    private static readonly DateTimeOffset Now = new(2026, 10, 14, 11, 0, 0, TimeSpan.Zero);

    private static AlertDto Alert(AlertType type, AlertSeverity severity = AlertSeverity.Critical) => new(
        Guid.NewGuid(), type, severity, "серверный текст", null, Guid.Parse("10000000-0000-0000-0000-000000000009"), "Лосось охл.",
        null, null, Now, null, null, true, MeasureUnit.Gram, null, null, null, null);

    [Fact]
    public void Expiring_soon_critical_goes_to_kitchen_and_warehouse()
    {
        var alert = Alert(AlertType.ExpiringSoon) with { Quantity = 2400, ExpiresAt = new DateTimeOffset(2026, 10, 14, 23, 0, 0, TimeSpan.FromHours(3)) };

        var message = PushTexts.Build(alert, "ru", Now)!;

        Assert.Equal(PushAudience.Roles(alert), [UserRole.Warehouse, UserRole.Chef, UserRole.Manager]);
        Assert.Equal("Скоро испортится: Лосось охл.", message.Title);
        Assert.Equal("2,4 кг — сегодня в 23:00. Пустите в работу или спишите.", message.Body);
        Assert.Equal("/stock?show=expiring", message.Url);
        Assert.Equal(AlertSeverity.Critical, message.Severity);
    }

    [Fact]
    public void Expiring_soon_warning_is_not_pushed_only_shown_in_the_app()
    {
        Assert.Empty(PushAudience.Roles(Alert(AlertType.ExpiringSoon, AlertSeverity.Warning)));
    }

    [Fact]
    public void Expired_tells_whether_it_was_written_off_automatically()
    {
        var auto = PushTexts.Build(Alert(AlertType.Expired) with { Quantity = 300, AutoWrittenOff = true }, "ru", Now)!;
        var manual = PushTexts.Build(Alert(AlertType.Expired) with { Quantity = 300, AutoWrittenOff = false }, "en", Now)!;

        Assert.Equal("Испортилось: Лосось охл.", auto.Title);
        Assert.Equal("300 г списано автоматически.", auto.Body);
        Assert.Equal("Spoiled: Лосось охл.", manual.Title);
        Assert.Equal("300 g — write it off.", manual.Body);
        Assert.Equal("/stock?show=spoiled", manual.Url);
        Assert.Equal(PushAudience.Roles(Alert(AlertType.Expired)), [UserRole.Warehouse, UserRole.Manager]);
    }

    [Fact]
    public void Low_stock_goes_to_whoever_orders_supplies()
    {
        var alert = Alert(AlertType.LowStock, AlertSeverity.Warning) with { Unit = MeasureUnit.Milliliter, Quantity = 900, LimitQty = 2000 };

        var message = PushTexts.Build(alert, "ru", Now)!;

        Assert.Equal(PushAudience.Roles(alert), [UserRole.Warehouse, UserRole.Manager]);
        Assert.Equal("Мало: Лосось охл.", message.Title);
        Assert.Equal("Осталось 900 мл при норме 2 л. Пора заказать.", message.Body);
        Assert.Equal("/supplies", message.Url);
    }

    [Fact]
    public void Stop_list_goes_to_the_kitchen()
    {
        var on = Alert(AlertType.StopListOn) with { DishId = Guid.NewGuid(), DishName = "Капрезе", ProductName = "Моцарелла" };
        var manual = on with { ProductName = null };
        var off = Alert(AlertType.StopListOff, AlertSeverity.Info) with { DishId = on.DishId, DishName = "Капрезе" };

        Assert.Equal(PushAudience.Roles(on), [UserRole.Chef, UserRole.Manager]);
        Assert.Equal("Нельзя заказать: Капрезе", PushTexts.Build(on, "ru", Now)!.Title);
        Assert.Equal("Закончилось: Моцарелла.", PushTexts.Build(on, "ru", Now)!.Body);
        Assert.Equal("Убрано из меню вручную.", PushTexts.Build(manual, "ru", Now)!.Body);
        Assert.Equal("Back on the menu: Капрезе", PushTexts.Build(off, "en", Now)!.Title);
        Assert.Equal("/stoplist", PushTexts.Build(off, "ru", Now)!.Url);
    }

    [Fact]
    public void Same_subject_replaces_the_previous_notification()
    {
        var first = PushTexts.Build(Alert(AlertType.ExpiringSoon) with { ExpiresAt = Now.AddHours(5) }, "ru", Now)!;
        var second = PushTexts.Build(Alert(AlertType.ExpiringSoon) with { ExpiresAt = Now.AddHours(4) }, "ru", Now)!;

        Assert.Equal(first.Tag, second.Tag);
    }

    [Fact]
    public void Message_without_required_fields_is_skipped()
    {
        Assert.Null(PushTexts.Build(Alert(AlertType.StopListOn) with { DishName = null }, "ru", Now));
    }
}
