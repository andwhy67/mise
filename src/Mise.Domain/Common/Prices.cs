namespace Mise.Domain.Common;

/// <summary>
/// Цены людям — за кг, л или штуку; в расчётах — за базовую единицу (г, мл, шт).
/// 900 ₽/кг ⇄ 0,9 ₽/г: так цена партии умножается на количество в журнале движений без пересчётов.
/// </summary>
public static class Prices
{
    private static decimal Factor(MeasureUnit unit) => unit == MeasureUnit.Piece ? 1m : 1000m;

    public static decimal ToBase(decimal pricePerDisplayUnit, MeasureUnit unit) => pricePerDisplayUnit / Factor(unit);

    public static decimal ToDisplay(decimal pricePerBaseUnit, MeasureUnit unit) =>
        decimal.Round(pricePerBaseUnit * Factor(unit), 2, MidpointRounding.AwayFromZero);
}
