namespace Mise.Application.Common;

/// <summary>
/// Отображение и «сутки» — по Europe/Moscow (CLAUDE.md, правило 7). В России нет перехода на летнее время,
/// поэтому фиксированное смещение +3 надёжнее, чем зависимость от tzdata в контейнере.
/// </summary>
public static class MoscowTime
{
    public static readonly TimeSpan Offset = TimeSpan.FromHours(3);

    public static DateOnly DateOf(DateTimeOffset instant) => DateOnly.FromDateTime(instant.ToOffset(Offset).DateTime);

    /// <summary>Начало суток по Москве (как момент времени).</summary>
    public static DateTimeOffset DayStart(DateOnly date) => new DateTimeOffset(date.ToDateTime(TimeOnly.MinValue), Offset).ToUniversalTime();
}
