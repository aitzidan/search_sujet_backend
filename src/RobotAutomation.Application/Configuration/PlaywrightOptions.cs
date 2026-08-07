namespace RobotAutomation.Application.Configuration;

public sealed class PlaywrightOptions
{
    public const string SectionName = "Playwright";

    /// <summary>Must stay false: the operator authenticates by hand in the browser window.</summary>
    public bool Headless { get; set; } = true;

    public int SlowMoMs { get; set; }

    public int DefaultTimeoutMs { get; set; } = 15000;

    /// <summary>Ceiling for a whole run; must exceed the operator's login and one-time-code waits.</summary>
    public int RunTimeoutMs { get; set; } = 120000;

    public int MaxConcurrentRuns { get; set; } = 4;

    public int MaxStepRetries { get; set; } = 2;

    public string? ScreenshotDirectory { get; set; }

    public string ResolveArtifactDirectory() =>
        string.IsNullOrWhiteSpace(ScreenshotDirectory)
            ? Path.Combine(Path.GetTempPath(), "robot-automation", "screenshots")
            : ScreenshotDirectory;
}
