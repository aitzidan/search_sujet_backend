namespace RobotAutomation.Application.Robots.Abstractions;

/// <summary>
/// A named automation scenario: an ordered list of steps. Robots are registered as
/// <c>IEnumerable&lt;IRobot&gt;</c> so new scenarios appear in the API automatically.
/// </summary>
public interface IRobot
{
    /// <summary>Stable key used to launch this robot via the API, e.g. "dgi-login".</summary>
    string Key { get; }

    /// <summary>Friendly name for the UI.</summary>
    string DisplayName { get; }

    /// <summary>The steps, in execution order.</summary>
    IReadOnlyList<IRobotStep> Steps { get; }
}
