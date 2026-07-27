namespace RobotAutomation.Application.Robots.Abstractions;

/// <summary>
/// One discrete automation step. The legacy robot's per-URL step methods
/// (OpenMenuItem, CreationPeriode, UploadfileEDI, editTva, ...) become small, reusable,
/// independently-testable classes; a robot is just an ordered list of them.
/// </summary>
public interface IRobotStep
{
    /// <summary>Stable, human-readable name shown in the run log (one "lamp").</summary>
    string Name { get; }

    /// <summary>
    /// Whether the executor may retry this step (Polly). Steps that mutate server state
    /// non-idempotently — a login submit, or the eventual "submit for validation" — set this
    /// to <c>false</c> so a submission is never double-fired.
    /// </summary>
    bool Retryable => true;

    Task ExecuteAsync(RobotContext ctx, CancellationToken ct);
}
