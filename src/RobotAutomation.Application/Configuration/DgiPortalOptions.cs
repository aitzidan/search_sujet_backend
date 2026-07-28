namespace RobotAutomation.Application.Configuration;

/// <summary>
/// Everything a robot needs to know about the portal it drives, kept entirely in
/// configuration so that switching from the fake test portal to the real DGI site
/// is a config change (a different named section), not a code change.
///
/// Bound from <c>appsettings.json</c> "DgiPortals:{name}" as a named option and
/// selected per run via <see cref="StartRobotRunRequest.PortalName"/>.
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
    /// Selector that proves the landing page is ready after navigation. If null, the login flow
    /// falls back to the username input. For non-login portals (e.g. the rendez-vous site) set this
    /// to the first element the robot needs (e.g. the "Prendre un rendez-vous" link).
    /// </summary>
    public string? ReadySelector { get; set; }

    /// <summary>
    /// Safety switch for real-site robots: when true, the robot performs every step EXCEPT the final
    /// irreversible submit (e.g. booking a real appointment). Lets you validate the flow without
    /// creating real data. Shipped ON for the real "rdv" portal.
    /// </summary>
    public bool StopBeforeFinalSubmit { get; set; }

    /// <summary>"DomText" (default — the fake portal renders the CAPTCHA answer as visible text),
    /// "Ocr" (local Tesseract reads the image), or "Manual" (the robot pauses and waits for a human to
    /// type it into the visible, non-headless browser window).</summary>
    public string CaptchaMode { get; set; } = "DomText";

    /// <summary>
    /// How many times a login may be submitted when the portal rejects it — OCR misreads a distorted
    /// CAPTCHA some of the time, and each retry loads a fresh image. Kept deliberately low: a portal
    /// that counts these as failed sign-in attempts could lock the account.
    /// </summary>
    public int CaptchaMaxAttempts { get; set; } = 1;

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
    /// Default login credentials for this portal, read from configuration (never hardcoded in a robot
    /// or step). A run's <c>Parameters</c> ("username"/"password") still take precedence when supplied.
    /// </summary>
    public CredentialsOptions Credentials { get; set; } = new();

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
/// CSS/text/role selectors for the elements a login robot touches. CSS only — never
/// XPath — because Playwright's CSS engine auto-pierces open shadow DOM (the Angular MFE
/// renders inside a shadow root) whereas XPath does not.
/// </summary>
public sealed class SelectorMap
{
    public string UsernameInput { get; set; } = "";
    public string PasswordInput { get; set; } = "";

    /// <summary>Element whose visible text is the CAPTCHA challenge to read (fake portal).</summary>
    public string CaptchaChallenge { get; set; } = "";
    public string CaptchaInput { get; set; } = "";

    public string SubmitButton { get; set; } = "";

    /// <summary>Element proving the success page rendered (used by the "SelectorVisible" success rule).</summary>
    public string SuccessIndicator { get; set; } = "";
}

/// <summary>
/// Generalizes the legacy robot's hard-coded SweetAlert2 success detection
/// ("read h2#swal2-title, contains 'Succès' => success, else div#swal2-content is the error,
/// click .swal2-actions button to dismiss") into a data-driven rule.
/// </summary>
public sealed class SuccessRuleOptions
{
    /// <summary>"SelectorVisible" (fake portal success page), "PopupTitleContains" (real DGI SweetAlert2),
    /// or "AwaitHidden" (an Angular SPA login: wait for the login form to disappear).</summary>
    public string Mode { get; set; } = "SelectorVisible";

    // Used by PopupTitleContains:
    public string? TitleSelector { get; set; }
    public string? ContentSelector { get; set; }
    public string? SuccessText { get; set; }
    public string? DismissSelector { get; set; }

    /// <summary>Used by AwaitHidden: the selector (e.g. the login form container) expected to
    /// disappear once login succeeds. On timeout, <see cref="ContentSelector"/> (if set and visible)
    /// is read as the error message.</summary>
    public string? HiddenSelector { get; set; }
}

/// <summary>Default username/password for a portal, sourced from configuration only.</summary>
public sealed class CredentialsOptions
{
    public string? Username { get; set; }
    public string? Password { get; set; }
}
