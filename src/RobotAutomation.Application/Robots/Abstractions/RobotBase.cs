namespace RobotAutomation.Application.Robots.Abstractions;

/// <summary>
/// Base class for robots. The only thing a concrete robot writes is <see cref="BuildSteps"/> —
/// an ordered list composed from reusable step classes. Adding or reordering a scenario needs
/// no change here: create a subclass, return a new list, register it in DI.
/// </summary>
public abstract class RobotBase : IRobot
{
    private IReadOnlyList<IRobotStep>? _steps;

    public abstract string Key { get; }
    public abstract string DisplayName { get; }

    protected abstract IEnumerable<IRobotStep> BuildSteps();

    public IReadOnlyList<IRobotStep> Steps => _steps ??= BuildSteps().ToList();
}
