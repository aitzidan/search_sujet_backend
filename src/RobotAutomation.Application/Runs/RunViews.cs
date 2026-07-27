namespace RobotAutomation.Application.Runs;

/// <summary>Read model for one step in a run's log.</summary>
public sealed record StepLogView(
    int Order,
    string StepName,
    string Status,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    string? Message,
    string? ScreenshotPath,
    string? CurrentUrl);

/// <summary>Full read model for a run (status + step log), returned by GET /api/robot-runs/{id}.</summary>
public sealed record RobotRunView(
    Guid RunId,
    string RobotKey,
    string PortalName,
    string Status,
    string? CurrentStep,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? StartedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    string? ErrorMessage,
    IReadOnlyList<StepLogView> Steps,
    IReadOnlyList<string> Screenshots,
    IReadOnlyDictionary<string, string?> Data);

/// <summary>Compact read model for listing concurrent runs.</summary>
public sealed record RunSummaryView(
    Guid RunId,
    string RobotKey,
    string Status,
    string? CurrentStep,
    DateTimeOffset? StartedAtUtc);

public static class RunViewMapper
{
    public static RobotRunView ToView(this RobotRun run) => new(
        run.RunId,
        run.RobotKey,
        run.PortalName,
        run.Status.ToString(),
        run.CurrentStepName,
        run.CreatedAtUtc,
        run.StartedAtUtc,
        run.CompletedAtUtc,
        run.ErrorMessage,
        run.Steps.Select(s => new StepLogView(
            s.Order, s.StepName, s.Status.ToString(), s.StartedAtUtc,
            s.CompletedAtUtc, s.Message, s.ScreenshotPath, s.CurrentUrl)).ToList(),
        run.Screenshots,
        run.Data);

    public static RunSummaryView ToSummary(this RobotRun run) => new(
        run.RunId, run.RobotKey, run.Status.ToString(), run.CurrentStepName, run.StartedAtUtc);
}
