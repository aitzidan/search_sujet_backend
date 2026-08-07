namespace RobotAutomation.Application.Robots.Abstractions;

public interface IRobot
{
    string Key { get; }

    string DisplayName { get; }

    IReadOnlyList<IRobotStep> Steps { get; }
}
