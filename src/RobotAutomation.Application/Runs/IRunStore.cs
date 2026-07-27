using RobotAutomation.Domain.Enums;

namespace RobotAutomation.Application.Runs;

/// <summary>
/// Stores run state and cancellation handles. In-memory for the PoC (behind this seam) so a later
/// EF-backed store can persist runs — the analog of the legacy <c>SuiviTeledeclaration</c> table —
/// with no change to Application.
/// </summary>
public interface IRunStore
{
    /// <summary>Create and register a new run in the <see cref="RobotStatus.Pending"/> state.</summary>
    RobotRun Create(Guid runId, string robotKey, string portalName);

    /// <summary>A consistent, detached copy for readers (the API). Null if unknown.</summary>
    RobotRun? Get(Guid runId);

    /// <summary>The canonical, mutable instance for the executor. Null if unknown.</summary>
    RobotRun? GetLive(Guid runId);

    /// <summary>Detached copies, newest first, optionally filtered by status.</summary>
    IReadOnlyCollection<RobotRun> List(RobotStatus? statusFilter = null);

    void RegisterCancellation(Guid runId, CancellationTokenSource cts);

    /// <summary>Signal cancellation for a running run. Returns false if unknown or already finished.</summary>
    bool TryCancel(Guid runId);

    void RemoveCancellation(Guid runId);
}
