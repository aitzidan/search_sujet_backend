namespace RobotAutomation.Application.Robots;

/// <summary>
/// The minimal browser-page vocabulary a robot step needs. This is the seam that keeps
/// Playwright out of Application/Domain: Infrastructure implements it over Playwright's
/// <c>IPage</c>, and steps/robots depend only on this interface (so they stay unit-testable
/// with a fake page, and the underlying driver could be swapped without touching robots).
///
/// Every method takes a <see cref="CancellationToken"/> so a run can be cancelled mid-step.
/// Implementations use auto-waiting locators (no fixed sleeps) — the modern replacement for
/// the legacy robot's <c>Thread.Sleep(1000/2000)</c> pacing.
/// </summary>
public interface IRobotPage
{
    /// <summary>Current page URL (constant for the Noop-routing MFE; meaningful for the real DGI).</summary>
    string Url { get; }

    Task GotoAsync(string url, string waitUntil, CancellationToken ct);

    /// <summary>Fills the first element matching <paramref name="selector"/> (e.g. a calendar/list selector that legitimately matches several rows — takes the first).</summary>
    Task FillAsync(string selector, string value, CancellationToken ct);

    /// <summary>Clicks the first element matching <paramref name="selector"/> — e.g. the first available date/time-slot when several are valid choices.</summary>
    Task ClickAsync(string selector, CancellationToken ct);

    /// <summary>Select an &lt;option&gt; by its value in a &lt;select&gt; (régime, mois, année…).</summary>
    Task SelectOptionAsync(string selector, string value, CancellationToken ct);

    /// <summary>Select an &lt;option&gt; by its visible label — robust against generated ids/values (e.g. PRADO on the real site).</summary>
    Task SelectOptionByLabelAsync(string selector, string label, CancellationToken ct);

    /// <summary>Whether a &lt;select&gt; currently holds a real (non-placeholder) selection — used to detect
    /// dropdowns silently reset by a later AJAX re-render (e.g. PRADO replacing a whole form panel).</summary>
    Task<bool> HasSelectedOptionAsync(string selector, CancellationToken ct);

    /// <summary>Whether a checkbox/radio is currently checked.</summary>
    Task<bool> IsCheckedAsync(string selector, CancellationToken ct);

    /// <summary>
    /// Whether the first element matching <paramref name="selector"/> is disabled — the <c>disabled</c>
    /// attribute, <c>aria-disabled</c>, or an ancestor disabled <c>&lt;fieldset&gt;</c>.
    ///
    /// Used as an outcome signal, not just a guard: a portal that greys out its submit once a form is
    /// saved is telling you the save landed, which some pages do instead of showing any message. Also
    /// worth checking before a click — clicking a disabled element makes Playwright wait for it to become
    /// enabled and then fail on timeout, which says nothing about why.
    /// </summary>
    Task<bool> IsDisabledAsync(string selector, CancellationToken ct);

    /// <summary>
    /// Whether the first element matching <paramref name="selector"/> can actually be typed into — enabled
    /// <b>and</b> not <c>readonly</c>.
    ///
    /// Not the negation of <see cref="IsDisabledAsync"/>: a <c>readonly</c> input is reported as perfectly
    /// enabled, yet filling it throws "element is not editable". This portal family uses exactly that to lock
    /// a form — the legacy robot greys its own fields with <c>setAttribute("readOnly", "true")</c>
    /// (winTeleDeclaration.xaml.cs:1852) — so "is this cell mine to fill?" has to be asked this way. Returns
    /// false for an element that is not there.
    /// </summary>
    Task<bool> IsEditableAsync(string selector, CancellationToken ct);

    /// <summary>Current value of the first element matching <paramref name="selector"/> (an input/textarea), or null if absent.</summary>
    Task<string?> GetValueAsync(string selector, CancellationToken ct);

    /// <summary>Waits until the first element matching <paramref name="selector"/> has a non-empty value — e.g. a human
    /// typing a CAPTCHA answer into a visible, non-headless browser — then returns it.</summary>
    Task<string> WaitForNonEmptyValueAsync(string selector, int timeoutMs, CancellationToken ct);

    /// <summary>Count elements matching the selector (used to know how many rows already exist).</summary>
    Task<int> CountAsync(string selector, CancellationToken ct);

    /// <summary>Visible text of the first element matching <paramref name="selector"/>, or null if absent.</summary>
    Task<string?> GetTextAsync(string selector, CancellationToken ct);

    /// <summary>An attribute of the first element matching <paramref name="selector"/> (e.g. an image's
    /// <c>src</c>), or null if the element or attribute is absent.</summary>
    Task<string?> GetAttributeAsync(string selector, string attribute, CancellationToken ct);

    Task<bool> IsVisibleAsync(string selector, CancellationToken ct);

    /// <summary>
    /// Whether the first element matching <paramref name="selector"/> currently overlaps the viewport.
    ///
    /// Needed because "visible" and "clickable where it is" are not the same thing: an off-canvas panel
    /// (a slide-in menu parked at <c>translateX(-100%)</c>) is reported visible — it has a non-empty box and
    /// is not <c>display:none</c> — yet every click on it fails with "element is outside of the viewport",
    /// because scrolling cannot bring it in. This is the test that tells a folded panel from an open one.
    /// </summary>
    Task<bool> IsInViewportAsync(string selector, CancellationToken ct);

    /// <summary>
    /// Scrolls the window back to the top of the document.
    ///
    /// Not needed to reach a button — Playwright scrolls an element into view before clicking it. It is
    /// needed for this portal's slide-in navigation, which is positioned against the top of the document:
    /// opened from a page scrolled to its footer, the panel unfolds *above* the viewport and its entries
    /// are unreachable where they are (see <see cref="IsInViewportAsync"/>), which no amount of further
    /// scrolling fixes because the click target moves with the page.
    /// </summary>
    Task ScrollToTopAsync(CancellationToken ct);

    Task WaitForSelectorAsync(string selector, int timeoutMs, CancellationToken ct);

    /// <summary>Wait for an element to be hidden/absent (e.g. an AJAX loading spinner to disappear).</summary>
    Task WaitForHiddenAsync(string selector, int timeoutMs, CancellationToken ct);

    /// <summary>Wait for the URL to match a glob/substring. Used by the real DGI (its hash route changes); a no-op-friendly fallback for the MFE.</summary>
    Task WaitForUrlAsync(string urlPattern, int timeoutMs, CancellationToken ct);

    /// <summary>Set a file on an &lt;input type=file&gt; — replaces the legacy SendKeys-into-OS-dialog upload.</summary>
    Task SetInputFilesAsync(string selector, string filePath, CancellationToken ct);

    /// <summary>Capture a full-page screenshot to <paramref name="filePath"/>; returns the path written.</summary>
    Task<string> ScreenshotAsync(string filePath, CancellationToken ct);

    /// <summary>Screenshot of just the first element matching <paramref name="selector"/> (e.g. a CAPTCHA
    /// image), as PNG bytes — works whether the element is a data-URI image or a normally loaded one.</summary>
    Task<byte[]> ScreenshotElementAsync(string selector, CancellationToken ct);
}
