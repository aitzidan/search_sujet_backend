namespace RobotAutomation.Application.Robots.Abstractions;

public abstract class RobotBase : IRobot
{
    private IReadOnlyList<IRobotStep>? _steps;

    public abstract string Key { get; }
    public abstract string DisplayName { get; }

    protected abstract IEnumerable<IRobotStep> BuildSteps();

    public IReadOnlyList<IRobotStep> Steps => _steps ??= BuildSteps().ToList();
}
