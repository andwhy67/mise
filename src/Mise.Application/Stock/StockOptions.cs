using Mise.Domain.Expiry;

namespace Mise.Application.Stock;

/// <summary>Настройки склада (секция конфигурации "Stock").</summary>
public sealed class StockOptions
{
    public const string SectionName = "Stock";

    public ShelfLifeOptions ShelfLife { get; set; } = new();

    /// <summary>
    /// «Спасено»: расход в заказ из партии, которой оставалось не больше стольких часов, — продукт продан, а не списан.
    /// 48 ч — граница «скоро» в интерфейсе (DESIGN.md §1.1).
    /// </summary>
    public decimal RescueWindowHours { get; set; } = 48m;

    public TimeSpan RescueWindow => TimeSpan.FromHours((double)RescueWindowHours);

    public sealed class ShelfLifeOptions
    {
        public decimal WarningFraction { get; set; } = ShelfLifeThresholds.Default.WarningFraction;
        public decimal WarningHours { get; set; } = ShelfLifeThresholds.Default.WarningHours;
        public decimal CriticalFraction { get; set; } = ShelfLifeThresholds.Default.CriticalFraction;
        public decimal CriticalHours { get; set; } = ShelfLifeThresholds.Default.CriticalHours;

        public ShelfLifeThresholds ToThresholds() =>
            new ShelfLifeThresholds(WarningFraction, WarningHours, CriticalFraction, CriticalHours).Validate();
    }
}
