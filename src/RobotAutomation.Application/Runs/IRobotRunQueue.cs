namespace RobotAutomation.Application.Runs;

public sealed record RobotRunRequest(
    Guid RunId,
    string RobotKey,
    string PortalName,
    IReadOnlyDictionary<string, string?> Parameters);

public interface IRobotRunQueue
{
    ValueTask EnqueueAsync(RobotRunRequest request, CancellationToken ct = default);
    ValueTask<RobotRunRequest> DequeueAsync(CancellationToken ct);
}
