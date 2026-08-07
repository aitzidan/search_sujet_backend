namespace RobotAutomation.Application.Runs;

public sealed record StepLogView(
    int Order,
    string StepName,
    string Status,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    string? Message,
    string? ScreenshotPath,
    string? CurrentUrl);

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
