using RobotAutomation.Application.Robots.Abstractions;
using RobotAutomation.Application.Runs;

namespace RobotAutomation.Application.Execution;

public interface IRobotExecutor
{
    Task ExecuteAsync(IRobot robot, RobotRun run, RobotContext ctx, CancellationToken ct);
}
