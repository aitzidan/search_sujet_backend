namespace RobotAutomation.Application.Runs;

/// <summary>
/// A run request handed to the background worker. Carries the credentials so they never touch
/// the run store — they exist only on this in-memory message and in the transient RobotContext.
/// </summary>
public sealed record RobotRunRequest(
    Guid RunId,
    string RobotKey,
    string PortalName,
    IReadOnlyDictionary<string, string?> Parameters);

/// <summary>
/// Decouples "start a run" (returns immediately) from "execute a run" (the worker). Backed by a
/// bounded in-memory channel; a durable queue could replace it behind this seam.
/// </summary>
public interface IRobotRunQueue
{
    ValueTask EnqueueAsync(RobotRunRequest request, CancellationToken ct = default);
    ValueTask<RobotRunRequest> DequeueAsync(CancellationToken ct);
}
