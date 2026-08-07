using FluentValidation;
using MediatR;
using RobotAutomation.Application.Robots;

namespace RobotAutomation.Application.Runs;

public sealed record StartRobotRunCommand(
    string RobotKey,
    string? PortalName,
    IReadOnlyDictionary<string, string?>? Parameters) : IRequest<StartRobotRunResult>;

public sealed record StartRobotRunResult(Guid RunId, string Status, string StatusUrl);

public sealed class StartRobotRunCommandValidator : AbstractValidator<StartRobotRunCommand>
{
    public StartRobotRunCommandValidator()
    {
        RuleFor(x => x.RobotKey).NotEmpty().WithMessage("robotKey is required.");
    }
}

internal sealed class StartRobotRunHandler : IRequestHandler<StartRobotRunCommand, StartRobotRunResult>
{
    private const string DefaultPortal = "real";

    private readonly IRobotCatalog _catalog;
    private readonly IRunStore _store;
    private readonly IRobotRunQueue _queue;

    public StartRobotRunHandler(IRobotCatalog catalog, IRunStore store, IRobotRunQueue queue)
    {
        _catalog = catalog;
        _store = store;
        _queue = queue;
    }

    public async Task<StartRobotRunResult> Handle(StartRobotRunCommand command, CancellationToken ct)
    {
        _ = _catalog.GetRequired(command.RobotKey);

        var portal = string.IsNullOrWhiteSpace(command.PortalName) ? DefaultPortal : command.PortalName!;
        var runId = Guid.NewGuid();
        var run = _store.Create(runId, command.RobotKey, portal);

        var parameters = command.Parameters ?? new Dictionary<string, string?>();
        await _queue.EnqueueAsync(new RobotRunRequest(runId, command.RobotKey, portal, parameters), ct);

        return new StartRobotRunResult(runId, run.Status.ToString(), $"/api/robot-runs/{runId}");
    }
}
