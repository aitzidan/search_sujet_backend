namespace RobotAutomation.Application.Configuration;

/// <summary>
/// Everything a robot needs to know about the portal it drives, kept entirely in configuration so that
/// retuning the flow against a redesigned DOM — or pointing it at another portal — is a config change
/// (a different named section), not a code change.
///
/// Bound from <c>appsettings.json</c> "DgiPortals:{name}" as a named option and
/// selected per run via <c>StartRobotRunCommand.PortalName</c>.
/// </summary>
public sealed class DgiPortalOptions
{
    /// <summary>Origin/root the robot navigates to, e.g. "http://localhost:4201/".</summary>
    public string BaseUrl { get; set; } = "";

    /// <summary>Route appended to <see cref="BaseUrl"/>, e.g. "#/dgi/login" (mirrors the real DGI hash route).</summary>
    public string InitialRoute { get; set; } = "";

    /// <summary>Playwright "wait until" state after navigation: Load | DOMContentLoaded | NetworkIdle.</summary>
    public string NavigationWaitUntil { get; set; } = "NetworkIdle";

    /// <summary>
    /// Selector that proves the landing page is ready after navigation. If null, the flow falls back to
    /// the username input — a best-effort check, since a run with a reused session may land straight in
    /// the authenticated application instead of the login form.
    /// </summary>
    public string? ReadySelector { get; set; }

    /// <summary>
    /// Safety switch: when true, the robot performs every step EXCEPT the irreversible writes — deleting
    /// a pending declaration and sending an EDI archive. Lets you validate selectors and inputs against
    /// the live portal without changing anything on the taxpayer's account.
    /// </summary>
    public bool StopBeforeFinalSubmit { get; set; }

    /// <summary>
    /// How long a step may wait for the operator to type something into the visible browser (login,
    /// CAPTCHA, a one-time code from an e-mail). Generous by design — it is waiting on a person, not a
    /// page. <c>Playwright:RunTimeoutMs</c> must exceed the sum of these waits or the run is killed first.
    /// </summary>
    public int ManualInputTimeoutMs { get; set; } = 300_000;

    /// <summary>
    /// Hard cap on how many existing declarations a single run may delete to clear the way for a new one.
    /// Deletion is irreversible on a real tax portal, so the robot stops at this many and reports what it
    /// left behind rather than emptying a table it may have misread. Raise it only for a portal whose list
    /// legitimately holds several pending declarations.
    /// </summary>
    public int MaxDeclarationDeletions { get; set; } = 1;

    /// <summary>
    /// Carry the browser session (cookies + localStorage) across runs for this portal, so a login one
    /// run performed can be reused by the next instead of re-authenticating every time. Intended for
    /// DEVELOPMENT against portals with an interactive login (CAPTCHA, one-time codes).
    ///
    /// How far it gets you depends on the portal: reuse lasts only as long as the portal considers the
    /// session valid, so an idle timeout or a short-lived server session will still force a fresh login
    /// (and whether it also skips a one-time-code challenge depends on whether that portal issues a
    /// longer-lived "trusted device" cookie). Robots must therefore tolerate either landing page.
    ///
    /// Off by default — with it on, runs are no longer isolated from each other.
    /// </summary>
    public bool ReuseSession { get; set; }

    /// <summary>
    /// Where the reused session is stored. Blank => a per-portal file under the system temp folder.
    /// The file contains live authentication cookies: treat it as a secret and never commit it.
    /// </summary>
    public string? SessionStatePath { get; set; }

    public SelectorMap Selectors { get; set; } = new();

    public SuccessRuleOptions SuccessRule { get; set; } = new();

    /// <summary>
    /// Additional named selectors for the post-login screens (menu, declaration, imported products).
    /// Kept as a map — rather than fixed properties — so new robot scenarios can add selectors via
    /// config without changing this class. Steps read them through <see cref="Element"/>.
    /// </summary>
    public Dictionary<string, string> Elements { get; set; } = new();

    public string FullUrl => BaseUrl.TrimEnd('/') + "/" + InitialRoute.TrimStart('/');

    /// <summary>Look up a named selector; throws a clear error if it is missing from config.</summary>
    public string Element(string key) =>
        Elements.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : throw new InvalidOperationException($"Selector '{key}' is not configured for this portal.");
}

/// <summary>
/// The two selectors of the login screen the robot still reads — it does not fill that form (the operator
/// does), it only needs to know which page is on screen. CSS only, never XPath, because Playwright's CSS
/// engine auto-pierces open shadow DOM whereas XPath does not.
/// </summary>
public sealed class SelectorMap
{
    /// <summary>Fallback readiness check after navigation when <see cref="DgiPortalOptions.ReadySelector"/>
    /// is not set.</summary>
    public string UsernameInput { get; set; } = "";

    /// <summary>The one-time-code page (<c>app-codeacces</c>): its appearance means the identifier,
    /// password and CAPTCHA were accepted, and its disappearance means the code was.</summary>
    public string SuccessIndicator { get; set; } = "";
}

/// <summary>How the robot recognises that the operator's manual login went through.</summary>
public sealed class SuccessRuleOptions
{
    /// <summary>The login form container (<c>app-login</c>), expected to disappear once the credentials
    /// are accepted. Watched alongside <see cref="SelectorMap.SuccessIndicator"/>, so an account that is
    /// never challenged for a one-time code still flows through.</summary>
    public string? HiddenSelector { get; set; }
}
