namespace RobotAutomation.Application.Configuration;

/// <summary>
/// Browser/runtime tuning for the Playwright engine, bound from "Playwright" in appsettings.
/// </summary>
public sealed class PlaywrightOptions
{
    public const string SectionName = "Playwright";

    /// <summary>Headless by default; set false to watch the robot drive a visible Chromium (great for demos).</summary>
    public bool Headless { get; set; } = true;

    /// <summary>Slow every Playwright action by N ms — useful when demoing in headed mode.</summary>
    public int SlowMoMs { get; set; }

    /// <summary>Default per-action / per-wait timeout in ms.</summary>
    public int DefaultTimeoutMs { get; set; } = 15000;

    /// <summary>Hard ceiling for a whole run in ms; prevents a hung page pinning a browser context.</summary>
    public int RunTimeoutMs { get; set; } = 120000;

    /// <summary>Max browser contexts (independent runs) executing at once.</summary>
    public int MaxConcurrentRuns { get; set; } = 4;

    /// <summary>Retry attempts applied to retryable steps (Polly). 0 disables retries.</summary>
    public int MaxStepRetries { get; set; } = 2;

    /// <summary>Where per-run screenshots are written. Null => a folder under the system temp path.</summary>
    public string? ScreenshotDirectory { get; set; }
}
