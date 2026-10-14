using FluentValidation;
using Mise.Application.Common;
using Mise.Application.Realtime;
using Mise.Domain.Abstractions;

namespace Mise.Application.Simulator;

/// <param name="Online">Симулятор присылает heartbeat (ключ статуса жив).</param>
public sealed record SimulatorStateDto(SimulatorControl Control, bool Online, SimulatorStatus? Status);

public sealed record StartSimulatorRequest(SimulatorScenario? Scenario = null, int? Speed = null, int? Seed = null);

public sealed record SetScenarioRequest(SimulatorScenario Scenario);

public sealed record SetSpeedRequest(int Speed);

/// <summary>Хранилище управления и статуса симулятора (Redis).</summary>
public interface ISimulatorControlStore
{
    Task<SimulatorControl> GetControlAsync(CancellationToken ct);

    /// <summary>Атомарно меняет желаемое состояние (версия +1) и оповещает симулятор.</summary>
    Task<SimulatorControl> UpdateControlAsync(Func<SimulatorControl, SimulatorControl> change, CancellationToken ct);

    Task<SimulatorStatus?> GetStatusAsync(CancellationToken ct);
}

internal sealed class StartSimulatorRequestValidator : AbstractValidator<StartSimulatorRequest>
{
    public StartSimulatorRequestValidator()
    {
        RuleFor(x => x.Scenario).IsInEnum();
        RuleFor(x => x.Speed!.Value).InclusiveBetween(SimulatorProtocol.MinSpeed, SimulatorProtocol.MaxSpeed)
            .When(x => x.Speed is not null).OverridePropertyName(nameof(StartSimulatorRequest.Speed));
    }
}

internal sealed class SetScenarioRequestValidator : AbstractValidator<SetScenarioRequest>
{
    public SetScenarioRequestValidator() => RuleFor(x => x.Scenario).IsInEnum();
}

internal sealed class SetSpeedRequestValidator : AbstractValidator<SetSpeedRequest>
{
    public SetSpeedRequestValidator() =>
        RuleFor(x => x.Speed).InclusiveBetween(SimulatorProtocol.MinSpeed, SimulatorProtocol.MaxSpeed);
}

/// <summary>
/// Управление симулятором (PLAN §8): API хранит желаемое состояние, отдельный процесс симулятора его исполняет
/// и отчитывается heartbeat-ом. Изменения сразу уходят клиентам событием SimulatorStateChanged.
/// </summary>
public sealed class SimulatorService(
    ISimulatorControlStore store,
    IClock clock,
    IRealtimeNotifier notifier,
    IValidator<StartSimulatorRequest> startValidator,
    IValidator<SetScenarioRequest> scenarioValidator,
    IValidator<SetSpeedRequest> speedValidator)
{
    public async Task<SimulatorStateDto> GetStateAsync(CancellationToken ct)
    {
        var control = await store.GetControlAsync(ct);
        var status = await store.GetStatusAsync(ct);
        return new SimulatorStateDto(control, status is not null, status);
    }

    public async Task<SimulatorStateDto> StartAsync(StartSimulatorRequest request, CancellationToken ct)
    {
        await startValidator.EnsureValidAsync(request, ct);
        return await ChangeAsync(c => c with
        {
            Running = true,
            Scenario = request.Scenario ?? c.Scenario,
            Speed = request.Speed ?? c.Speed,
            Seed = request.Seed,
        }, ct);
    }

    public Task<SimulatorStateDto> StopAsync(CancellationToken ct) => ChangeAsync(c => c with { Running = false }, ct);

    public async Task<SimulatorStateDto> SetScenarioAsync(SetScenarioRequest request, CancellationToken ct)
    {
        await scenarioValidator.EnsureValidAsync(request, ct);
        return await ChangeAsync(c => c with { Scenario = request.Scenario }, ct);
    }

    public async Task<SimulatorStateDto> SetSpeedAsync(SetSpeedRequest request, CancellationToken ct)
    {
        await speedValidator.EnsureValidAsync(request, ct);
        return await ChangeAsync(c => c with { Speed = request.Speed }, ct);
    }

    private async Task<SimulatorStateDto> ChangeAsync(Func<SimulatorControl, SimulatorControl> change, CancellationToken ct)
    {
        var now = clock.UtcNow;
        await store.UpdateControlAsync(c => change(c) with { Version = c.Version + 1, UpdatedAt = now }, ct);
        var state = await GetStateAsync(ct);
        notifier.SimulatorStateChanged(state);
        return state;
    }
}
