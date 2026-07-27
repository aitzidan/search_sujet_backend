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
    /// <summary>"SelectorVisible" (fake portal success page) or "PopupTitleContains" (real DGI SweetAlert2).</summary>
    public string Mode { get; set; } = "SelectorVisible";

    // Used by PopupTitleContains:
    public string? TitleSelector { get; set; }
    public string? ContentSelector { get; set; }
    public string? SuccessText { get; set; }
    public string? DismissSelector { get; set; }
}
