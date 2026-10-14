using FluentValidation;
using FluentValidation.Results;
using Microsoft.EntityFrameworkCore;
using Mise.Application.Abstractions;
using Mise.Application.Common;
using Mise.Application.Menu;
using Mise.Domain.Abstractions;
using Mise.Domain.Common;
using Mise.Domain.Stock;

namespace Mise.Application.Stock;

/// <param name="Quantity">Сколько списать; не указано — весь остаток партии.</param>
/// <param name="Unit">Единица ввода количества; по умолчанию — базовая единица продукта.</param>
/// <param name="Reason">Причина — обязательна (попадает в журнал движений).</param>
public sealed record WriteOffRequest(decimal? Quantity, InputUnit? Unit, string Reason);

public sealed record WriteOffResultDto(
    Guid BatchId,
    Guid ProductId,
    string ProductName,
    MeasureUnit Unit,
    decimal WrittenOffQty,
    decimal RemainingQty,
    BatchStatus Status,
    Guid MovementId,
    string Reason,
    DateTimeOffset CreatedAt);

internal sealed class WriteOffRequestValidator : AbstractValidator<WriteOffRequest>
{
    public const int ReasonMaxLength = 500;

    public WriteOffRequestValidator()
    {
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(ReasonMaxLength);
        RuleFor(x => x.Quantity!.Value).IsQuantity().When(x => x.Quantity is not null).OverridePropertyName(nameof(WriteOffRequest.Quantity));
        RuleFor(x => x.Unit).IsInEnum();
    }
}

/// <summary>Ручное списание партии (бой, порча, инвентаризация): движение ManualWriteOff под блокировкой партии.</summary>
public sealed class WriteOffService(IMiseDbContext db, IStockLocks locks, IClock clock, StopListService stopList,
    IValidator<WriteOffRequest> validator)
{
    public async Task<WriteOffResultDto> WriteOffAsync(Guid batchId, WriteOffRequest request, CancellationToken ct)
    {
        await validator.EnsureValidAsync(request, ct);
        var reason = request.Reason.Trim();
        var now = clock.UtcNow;

        WriteOffResultDto result;
        await using (var transaction = await db.Database.BeginTransactionAsync(ct))
        {
            var batch = (await locks.LockBatchesAsync([batchId], ct)).SingleOrDefault()
                ?? throw new NotFoundException("Партия", batchId);
            var product = await db.Products.AsNoTracking().FirstAsync(p => p.Id == batch.ProductId, ct);

            decimal? quantity = null;
            if (request.Quantity is { } requested)
            {
                try
                {
                    quantity = UnitConverter.ToBase(requested, request.Unit ?? UnitConverter.DefaultInputFor(product.Unit), product.Unit);
                }
                catch (DomainException ex)
                {
                    throw new[] { new ValidationFailure(nameof(request.Quantity), ex.Message) { ErrorCode = ex.Code } }
                        .ToValidationException();
                }
            }

            if (!batch.CanBeWrittenOff)
                throw new ConflictException(Batch.Errors.NotWritable,
                    $"Партия в статусе {batch.Status} — списывать нечего.");
            if (quantity > batch.RemainingQty)
                throw new BusinessRuleException(Batch.Errors.InsufficientQuantity,
                    $"В партии осталось {batch.RemainingQty}, а списать требуется {quantity}.",
                    new Dictionary<string, object?> { ["remainingQty"] = batch.RemainingQty, ["requestedQty"] = quantity });

            var written = batch.WriteOff(quantity);
            var movement = StockMovement.ManualWriteOff(Guid.CreateVersion7(now), batch, written, reason, now);
            db.StockMovements.Add(movement);
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);

            result = new WriteOffResultDto(batch.Id, product.Id, product.Name, product.Unit, written, batch.RemainingQty,
                batch.Status, movement.Id, reason, now);
        }

        await stopList.TryRefreshAsync(ct);
        return result;
    }
}
