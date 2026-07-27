namespace RobotAutomation.Domain.Enums;

/// <summary>
/// Lifecycle of a single robot run. Mirrors the legacy WPF robot's persisted
/// progress model (<c>EtapeSuiviTeleDec</c>) collapsed to a run-level status.
/// </summary>
public enum RobotStatus
{
    Pending,
    Running,
    Succeeded,
    Failed,
    Cancelled
}
