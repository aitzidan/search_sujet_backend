namespace RobotAutomation.Application.Files;

/// <summary>
/// Supplies a path to a small sample EDI file for the upload step to demonstrate
/// <c>SetInputFiles</c> (the modern replacement for the legacy SendKeys-into-OS-dialog upload).
/// Implemented in Infrastructure (file IO stays out of Application).
/// </summary>
public interface ISampleFileProvider
{
    Task<string> GetSampleEdiFileAsync(CancellationToken ct);
}
