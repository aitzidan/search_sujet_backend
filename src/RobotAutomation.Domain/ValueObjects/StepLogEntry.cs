using RobotAutomation.Domain.Enums;

namespace RobotAutomation.Domain.ValueObjects;

/// <summary>
/// One entry in a run's execution log — a single automation step and its outcome.
/// Immutable: the executor replaces an entry (via <c>with</c>) when a step transitions,
/// so a concurrent reader always sees a whole, consistent entry.
/// </summary>
public sealed record StepLogEntry(
    int Order,
    string StepName,
    StepStatus Status,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    string? Message,
    string? ScreenshotPath,
    string? CurrentUrl);
