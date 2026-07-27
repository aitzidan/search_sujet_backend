namespace RobotAutomation.Domain.Common;

/// <summary>
/// Thrown when a requested resource (an unknown robot key or run id) does not exist.
/// Translated to HTTP 404 by the WebApi's global exception handler.
/// </summary>
public sealed class NotFoundException : Exception
{
    public NotFoundException(string message) : base(message) { }

    public static NotFoundException Robot(string key) =>
        new($"No robot is registered with key '{key}'.");

    public static NotFoundException Run(Guid runId) =>
        new($"No robot run exists with id '{runId}'.");
}
