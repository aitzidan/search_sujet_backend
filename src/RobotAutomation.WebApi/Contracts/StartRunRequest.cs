namespace RobotAutomation.WebApi.Contracts;

public sealed record StartRunRequest(
    string RobotKey,
    string? PortalName,
    Dictionary<string, string?>? Parameters);
