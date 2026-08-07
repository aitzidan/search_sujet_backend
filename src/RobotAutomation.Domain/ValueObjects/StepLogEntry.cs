using RobotAutomation.Domain.Enums;

namespace RobotAutomation.Domain.ValueObjects;

public sealed record StepLogEntry(
    int Order,
    string StepName,
    StepStatus Status,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    string? Message,
    string? ScreenshotPath,
    string? CurrentUrl);
