namespace RobotAutomation.Domain.Common;

public sealed class NotFoundException : Exception
{
    public NotFoundException(string message) : base(message) { }

    public static NotFoundException Robot(string key) =>
        new($"No robot is registered with key '{key}'.");

    public static NotFoundException Run(Guid runId) =>
        new($"No robot run exists with id '{runId}'.");
}
