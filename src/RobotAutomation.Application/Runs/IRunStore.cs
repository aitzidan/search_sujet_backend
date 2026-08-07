using RobotAutomation.Domain.Enums;

namespace RobotAutomation.Application.Runs;

public interface IRunStore
{
    RobotRun Create(Guid runId, string robotKey, string portalName);

    RobotRun? Get(Guid runId);

    RobotRun? GetLive(Guid runId);

    IReadOnlyCollection<RobotRun> List(RobotStatus? statusFilter = null);

    void RegisterCancellation(Guid runId, CancellationTokenSource cts);

    bool TryCancel(Guid runId);

    void RemoveCancellation(Guid runId);
}
