using System.Globalization;

namespace Mise.Domain.Common;

/// <summary>Человекочитаемые количества и сроки для объяснений и алертов (русский, Europe/Moscow не нужен — только длительности).</summary>
public static class RuFormat
{
    private static readonly CultureInfo Ru = CultureInfo.GetCultureInfo("ru-RU");

    /// <summary>180 г, 1,2 кг, 750 мл, 2 л, 3 шт.</summary>
    public static string Quantity(decimal value, MeasureUnit unit) => unit switch
    {
        MeasureUnit.Gram when Math.Abs(value) >= 1000 => $"{Number(value / 1000)} кг",
        MeasureUnit.Gram => $"{Number(value)} г",
        MeasureUnit.Milliliter when Math.Abs(value) >= 1000 => $"{Number(value / 1000)} л",
        MeasureUnit.Milliliter => $"{Number(value)} мл",
        _ => $"{Number(value)} шт",
    };

    /// <summary>40 мин, 4,5 ч, 12 ч.</summary>
    public static string Hours(decimal hours)
    {
        if (hours < 1)
            return $"{Math.Max(1, (int)decimal.Round(hours * 60)).ToString(Ru)} мин";
        var rounded = decimal.Round(hours, hours < 10 ? 1 : 0, MidpointRounding.AwayFromZero);
        return $"{rounded.ToString("0.#", Ru)} ч";
    }

    /// <summary>Родительный падеж после «до»: до 1 порции, до 2 порций, до 11 порций, до 21 порции.</summary>
    public static string PortionsGenitive(int count) =>
        $"{count} {(count % 10 == 1 && count % 100 != 11 ? "порции" : "порций")}";

    private static string Number(decimal value) =>
        decimal.Round(value, Math.Abs(value) >= 100 ? 0 : 1, MidpointRounding.AwayFromZero).ToString("0.#", Ru);
}
