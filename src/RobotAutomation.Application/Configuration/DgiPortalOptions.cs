namespace RobotAutomation.Application.Configuration;

public sealed class DgiPortalOptions
{
    public string BaseUrl { get; set; } = "";

    public string InitialRoute { get; set; } = "";

    public string NavigationWaitUntil { get; set; } = "NetworkIdle";

    public string? ReadySelector { get; set; }

    /// <summary>
    /// Dry-run: when true the robot performs every step EXCEPT the irreversible writes — deleting a
    /// pending declaration and sending an EDI archive — so the flow can be validated without changing
    /// anything on the taxpayer's account.
    /// </summary>
    public bool StopBeforeFinalSubmit { get; set; }

    /// <summary>How long a step may wait for the operator to type the login, the CAPTCHA or the one-time
    /// code. Generous by design: it is waiting on a person, not a page.</summary>
    public int ManualInputTimeoutMs { get; set; } = 300_000;

    /// <summary>
    /// Hard cap on how many existing declarations a single run may delete to clear the way for a new one.
    /// Deletion is irreversible on a real tax portal, so the robot stops at this many and reports what it
    /// left behind rather than emptying a table it may have misread.
    /// </summary>
    public int MaxDeclarationDeletions { get; set; } = 1;

    /// <summary>
    /// Carry the browser session across runs so a login one run performed can be reused by the next.
    /// Intended for development; with it on, runs are no longer isolated from each other.
    /// </summary>
    public bool ReuseSession { get; set; }

    /// <summary>Where the reused session is stored. The file contains live authentication cookies: treat
    /// it as a secret and never commit it.</summary>
    public string? SessionStatePath { get; set; }

    public SelectorMap Selectors { get; set; } = new();

    public SuccessRuleOptions SuccessRule { get; set; } = new();

    public Dictionary<string, string> Elements { get; set; } = new();

    public string FullUrl => BaseUrl.TrimEnd('/') + "/" + InitialRoute.TrimStart('/');

    public string Element(string key) =>
        Elements.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : throw new InvalidOperationException($"Selector '{key}' is not configured for this portal.");
}

/// <summary>
/// The two selectors of the login screen the robot reads. It does not fill that form — the operator
/// does — it only needs to know which page is on screen.
/// </summary>
public sealed class SelectorMap
{
    public string UsernameInput { get; set; } = "";

    /// <summary>The one-time-code page: its appearance means the identifier, password and CAPTCHA were
    /// accepted, and its disappearance means the code was.</summary>
    public string SuccessIndicator { get; set; } = "";
}

public sealed class SuccessRuleOptions
{
    /// <summary>The login form, expected to disappear once the credentials are accepted. Watched
    /// alongside <see cref="SelectorMap.SuccessIndicator"/> so an account that is never challenged for a
    /// one-time code still flows through.</summary>
    public string? HiddenSelector { get; set; }
}
