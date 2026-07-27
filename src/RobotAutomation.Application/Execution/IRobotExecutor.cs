using RobotAutomation.Application.Robots.Abstractions;
using RobotAutomation.Application.Runs;

namespace RobotAutomation.Application.Execution;

/// <summary>Runs a robot's ordered steps against a live page, updating the run state as it goes.</summary>
public interface IRobotExecutor
{
    Task ExecuteAsync(IRobot robot, RobotRun run, RobotContext ctx, CancellationToken ct);
}
