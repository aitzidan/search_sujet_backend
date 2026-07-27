namespace RobotAutomation.WebApi.Contracts;

/// <summary>
/// Request body for POST /api/robot-runs. <paramref name="Parameters"/> carries run inputs such as
/// { "username": "...", "password": "..." }; credentials are used in-memory and never persisted.
/// </summary>
public sealed record StartRunRequest(
    string RobotKey,
    string? PortalName,
    Dictionary<string, string?>? Parameters);
