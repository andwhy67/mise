using System.Text.Json;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.EntityFrameworkCore;
using Mise.Application.Abstractions;
using Mise.Application.Audit;
using Mise.Application.Common;
using Mise.Domain.Abstractions;
using Mise.Domain.Common;

namespace Mise.Application.Supplies;

/// <param name="Quantity">В базовой единице продукта (г, мл, шт).</param>
public sealed record SupplyRequestLineRequest(Guid ProductId, decimal Quantity);

/// <param name="Supplier">Кому заявка; пусто — «без поставщика» (у продуктов он не указан).</param>
public sealed record CreateSupplyRequestRequest(string? Supplier, IReadOnlyList<SupplyRequestLineRequest> Lines);

/// <param name="Price">Справочная цена за кг / л / шт; null — неизвестна.</param>
/// <param name="Cost">Ориентировочная сумма строки по справочной цене.</param>
public sealed record SupplyRequestLineDto(Guid ProductId, string ProductName, MeasureUnit Unit, decimal Quantity, decimal? Price, decimal? Cost);

/// <param name="EstimatedTotal">Сумма строк с известной ценой; ориентир, а не счёт поставщика.</param>
public sealed record SupplyRequestDto(
    string Number,
    DateTimeOffset CreatedAt,
    string? Supplier,
    IReadOnlyList<SupplyRequestLineDto> Lines,
    decimal EstimatedTotal,
    int UnpricedLines);

internal sealed class CreateSupplyRequestRequestValidator : AbstractValidator<CreateSupplyRequestRequest>
{
    public const int MaxLines = 100;

    public CreateSupplyRequestRequestValidator()
    {
        RuleFor(x => x.Supplier).MaximumLength(Limits.NameMaxLength);
        RuleFor(x => x.Lines)
            .NotEmpty().WithMessage("В заявке должен быть хотя бы один продукт.")
            .Must(l => l.Count <= MaxLines).WithMessage($"Не больше {MaxLines} строк в заявке.")
            .Must(l => l.Select(x => x.ProductId).Distinct().Count() == l.Count).WithMessage("Продукт указан дважды.");
        RuleForEach(x => x.Lines).ChildRules(line =>
        {
            line.RuleFor(l => l.ProductId).NotEmpty();
            line.RuleFor(l => l.Quantity).IsQuantity();
        });
    }
}

/// <summary>
/// Заявка поставщику из «Что заказать». Отправляет её человек (копирует текст или печатает) — система не ждёт
/// ответа поставщика и не ведёт статусов; заявка с номером остаётся в журнале действий (кто, когда, что).
/// </summary>
public sealed class SupplyRequestService(
    IMiseDbContext db,
    IClock clock,
    AuditService audit,
    IValidator<CreateSupplyRequestRequest> validator)
{
    public const string AuditAction = "supply_request.create";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<SupplyRequestDto> CreateAsync(CreateSupplyRequestRequest request, CancellationToken ct)
    {
        await validator.EnsureValidAsync(request, ct);
        var ids = request.Lines.Select(l => l.ProductId).ToList();
        var products = await db.Products.AsNoTracking().Where(p => ids.Contains(p.Id)).ToDictionaryAsync(p => p.Id, ct);
        var failures = request.Lines
            .Select((line, index) => (line, index))
            .Where(x => !products.ContainsKey(x.line.ProductId))
            .Select(x => new ValidationFailure($"Lines[{x.index}].ProductId", "Продукт не найден."))
            .ToList();
        if (failures.Count > 0)
            throw failures.ToValidationException();

        var now = clock.UtcNow;
        var lines = request.Lines.Select(l =>
            {
                var product = products[l.ProductId];
                var cost = product.ReferenceUnitCost is { } unitCost ? decimal.Round(l.Quantity * unitCost, 2, MidpointRounding.AwayFromZero) : (decimal?)null;
                var price = product.ReferenceUnitCost is { } c ? Prices.ToDisplay(c, product.Unit) : (decimal?)null;
                return new SupplyRequestLineDto(product.Id, product.Name, product.Unit, l.Quantity, price, cost);
            })
            .ToList();
        var supplier = string.IsNullOrWhiteSpace(request.Supplier) ? null : request.Supplier.Trim();
        // Номер читается человеком и уникален без отдельного счётчика: дата и время по Москве + 4 знака случайного хвоста.
        var number = $"REQ-{now.ToOffset(MoscowTime.Offset):yyyyMMdd-HHmm}-{Guid.NewGuid().ToString("N")[..4].ToUpperInvariant()}";
        var dto = new SupplyRequestDto(number, now, supplier, lines, lines.Sum(l => l.Cost ?? 0m), lines.Count(l => l.Cost is null));

        object details = new
        {
            number,
            supplier,
            lines = lines.Select(l => new { product = l.ProductName, qty = l.Quantity, unit = l.Unit.ToString() }),
            estimatedTotal = dto.EstimatedTotal,
        };
        // Журнал хранит до 4000 символов: длинная заявка — без строк, но с номером, поставщиком и итогом.
        if (JsonSerializer.Serialize(details, Json).Length > AuditService.DetailsMaxLength)
            details = new { number, supplier, lineCount = lines.Count, estimatedTotal = dto.EstimatedTotal };
        await audit.RecordAsync(AuditAction, "SupplyRequest", number, details, ct);
        return dto;
    }
}
