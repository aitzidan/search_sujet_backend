using System.Collections.Concurrent;
using RobotAutomation.Application.Runs;
using RobotAutomation.Domain.Enums;

namespace RobotAutomation.Infrastructure.Runs;

internal sealed class InMemoryRunStore : IRunStore
{
    private readonly ConcurrentDictionary<Guid, RobotRun> _runs = new();
    private readonly ConcurrentDictionary<Guid, CancellationTokenSource> _cancellations = new();

    public RobotRun Create(Guid runId, string robotKey, string portalName)
    {
        var run = new RobotRun
        {
            RunId = runId,
            RobotKey = robotKey,
            PortalName = portalName,
            CreatedAtUtc = DateTimeOffset.UtcNow
        };
        _runs[runId] = run;
        return run;
    }

    public RobotRun? Get(Guid runId) =>
        _runs.TryGetValue(runId, out var run) ? run.Snapshot() : null;

    public RobotRun? GetLive(Guid runId) =>
        _runs.GetValueOrDefault(runId);

    public IReadOnlyCollection<RobotRun> List(RobotStatus? statusFilter = null) =>
        _runs.Values
            .Where(r => statusFilter is null || r.Status == statusFilter)
            .OrderByDescending(r => r.CreatedAtUtc)
            .Select(r => r.Snapshot())
            .ToList();

    public void RegisterCancellation(Guid runId, CancellationTokenSource cts) =>
        _cancellations[runId] = cts;

    public bool TryCancel(Guid runId)
    {
        if (!_cancellations.TryGetValue(runId, out var cts)) return false;
        var run = GetLive(runId);
        if (run is null || run.Status is RobotStatus.Succeeded or RobotStatus.Failed or RobotStatus.Cancelled)
            return false;

        cts.Cancel();
        return true;
    }

    public void RemoveCancellation(Guid runId)
    {
        if (_cancellations.TryRemove(runId, out var cts)) cts.Dispose();
    }
}
