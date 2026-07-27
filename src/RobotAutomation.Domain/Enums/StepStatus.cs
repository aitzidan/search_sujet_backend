namespace RobotAutomation.Domain.Enums;

/// <summary>
/// Status of one step inside a run. The set of steps + their statuses is the
/// modern equivalent of the legacy robot's 8 progress "lamps" (<c>LstAvancement</c>).
/// </summary>
public enum StepStatus
{
    Pending,
    Running,
    Succeeded,
    Failed,
    Skipped
}
