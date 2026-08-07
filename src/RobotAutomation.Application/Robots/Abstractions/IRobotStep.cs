namespace RobotAutomation.Application.Robots.Abstractions;

public interface IRobotStep
{
    string Name { get; }

    /// <summary>
    /// Steps that are not idempotent — creating, saving or sending a declaration — set this to
    /// <c>false</c> so a submission is never double-fired on the portal.
    /// </summary>
    bool Retryable => true;

    Task ExecuteAsync(RobotContext ctx, CancellationToken ct);
}
