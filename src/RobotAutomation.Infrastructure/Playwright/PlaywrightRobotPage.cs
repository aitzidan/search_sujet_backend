using Microsoft.Playwright;
using RobotAutomation.Application.Robots;

namespace RobotAutomation.Infrastructure.Playwright;

/// <summary>
/// The only adapter that speaks Playwright. Implements the Application-defined <see cref="IRobotPage"/>
/// over a Playwright <see cref="IPage"/>. Uses CSS/text locators (which auto-pierce open shadow DOM —
/// the Angular MFE renders inside a shadow root) and Playwright's built-in auto-waiting (no fixed sleeps).
/// </summary>
internal sealed class PlaywrightRobotPage : IRobotPage
{
    private readonly IPage _page;

    public PlaywrightRobotPage(IPage page) => _page = page;

    public string Url => _page.Url;

    public Task GotoAsync(string url, string waitUntil, CancellationToken ct) =>
        _page.GotoAsync(url, new PageGotoOptions { WaitUntil = ParseWaitUntil(waitUntil) });

    public Task FillAsync(string selector, string value, CancellationToken ct) =>
        _page.Locator(selector).First.FillAsync(value);

    public Task ClickAsync(string selector, CancellationToken ct) =>
        _page.Locator(selector).First.ClickAsync();

    public Task SelectOptionAsync(string selector, string value, CancellationToken ct) =>
        _page.SelectOptionAsync(selector, value);

    public async Task SelectOptionByLabelAsync(string selector, string label, CancellationToken ct)
    {
        // WAIT for the matching <option> to exist (dependent dropdowns are AJAX-populated), matching by
        // visible text — exact first, then "contains". Normalization is case-, space- AND accent-insensitive
        // (NFD + strip diacritics): the real DGI site is full of accents ("DROITS COMPLÉMENTAIRES",
        // "L'oriental", "Région"), so a single 'é' vs 'e' must not break the match. Options inside
        // <optgroup> are handled automatically (el.options flattens them).
        //
        // Several DGI dropdowns collapse to a SINGLE real choice once upstream fields (Région/Direction/
        // Nature) narrow them down — e.g. "Vous êtes" offers only "Particulier" for one Direction and only
        // "Petite et moyenne entreprise" for another. Hardcoding one label is whack-a-mole, so when exactly
        // one non-placeholder option exists, that option IS the answer regardless of the requested label.
        // With 2+ real options, the requested label still decides via exact → contains matching.
        //
        // Then select by the option's value with Force (the Direction select is hidden behind Chosen, display:none).
        IJSHandle handle;
        try
        {
            handle = await _page.WaitForFunctionAsync(
                @"([sel, target]) => {
                    const el = document.querySelector(sel);
                    if (!el || !el.options) return null;
                    const M = new RegExp('[' + String.fromCharCode(768) + '-' + String.fromCharCode(879) + ']', 'g');
                    const norm = s => (s || '').normalize('NFD').replace(M, '')
                        .replace(/\s+/g, ' ').trim().toLowerCase();
                    const isPlaceholder = o => !o.value || o.value === '0' || /^s[ée]lectionnez/.test(norm(o.textContent));
                    const real = Array.from(el.options).filter(o => !isPlaceholder(o));
                    if (real.length === 1) return real[0].value;
                    const t = norm(target);
                    if (!t) return null;
                    let opt = real.find(o => norm(o.textContent) === t);
                    if (!opt) opt = real.find(o => norm(o.textContent).includes(t));
                    if (!opt) opt = real.find(o => norm(o.textContent).length > 2 && t.includes(norm(o.textContent)));
                    return opt ? opt.value : null;
                }",
                new object[] { selector, label },
                new PageWaitForFunctionOptions { Timeout = 15000 });
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            var available = await DescribeOptionsAsync(selector);
            throw new InvalidOperationException(
                $"Aucune option ne correspond à « {label} » dans « {selector} ». Options disponibles : {available}", ex);
        }

        var value = await handle.JsonValueAsync<string?>();
        if (string.IsNullOrEmpty(value))
        {
            var available = await DescribeOptionsAsync(selector);
            throw new InvalidOperationException(
                $"Aucune option ne correspond à « {label} » dans « {selector} ». Options disponibles : {available}");
        }

        await _page.SelectOptionAsync(selector, new SelectOptionValue { Value = value },
            new PageSelectOptionOptions { Force = true });

        // Best-effort visual refresh: Playwright's SelectOption already dispatches a real 'change' event
        // (that's what fires the site's Prado.CallbackRequest onchange handlers — the AJAX cascade works),
        // but the Chosen plugin occasionally needs its own explicit nudge to redraw the widget it renders
        // over a forced/programmatic selection. Harmless no-op wherever jQuery/Chosen isn't attached.
        try
        {
            await _page.EvaluateAsync(
                @"(sel) => {
                    const el = document.querySelector(sel);
                    if (el && window.jQuery) { try { window.jQuery(el).trigger('chosen:updated'); } catch (e) {} }
                }",
                selector);
        }
        catch { /* cosmetic only — never fail the step over this */ }
    }

    /// <summary>
    /// Lists the current option labels of a select, so a "no option matched" error tells the user exactly
    /// which values the (region/nature-dependent) dropdown actually offers instead of leaving them to guess.
    /// </summary>
    private async Task<string> DescribeOptionsAsync(string selector)
    {
        try
        {
            var labels = await _page.EvaluateAsync<string[]?>(
                @"(sel) => {
                    const el = document.querySelector(sel);
                    if (!el || !el.options) return null;
                    return Array.from(el.options)
                        .map(o => (o.textContent || '').replace(/\s+/g, ' ').trim())
                        .filter(t => t && !/^s[ée]lectionnez$/i.test(t));
                }",
                selector);
            if (labels is null) return "(sélecteur introuvable)";
            return labels.Length == 0 ? "(liste vide — pas encore chargée ?)" : string.Join(" | ", labels);
        }
        catch
        {
            return "(indisponible)";
        }
    }

    public Task<bool> HasSelectedOptionAsync(string selector, CancellationToken ct) =>
        _page.EvaluateAsync<bool>(
            @"(sel) => {
                const el = document.querySelector(sel);
                if (!el || !el.options || el.selectedIndex < 0) return false;
                const o = el.options[el.selectedIndex];
                if (!o) return false;
                if (!o.value || o.value === '0') return false;
                const norm = (o.textContent || '').trim().toLowerCase();
                return !/^s[ée]lectionnez/.test(norm);
            }",
            selector);

    public Task<bool> IsCheckedAsync(string selector, CancellationToken ct) =>
        _page.Locator(selector).First.IsCheckedAsync();

    public Task<int> CountAsync(string selector, CancellationToken ct) =>
        _page.Locator(selector).CountAsync();

    public async Task<string?> GetTextAsync(string selector, CancellationToken ct)
    {
        var locator = _page.Locator(selector).First;
        if (await locator.CountAsync() == 0) return null;
        return (await locator.TextContentAsync())?.Trim();
    }

    public Task<bool> IsVisibleAsync(string selector, CancellationToken ct) =>
        _page.Locator(selector).First.IsVisibleAsync();

    public Task WaitForSelectorAsync(string selector, int timeoutMs, CancellationToken ct) =>
        _page.Locator(selector).First.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = timeoutMs
        });

    public Task WaitForHiddenAsync(string selector, int timeoutMs, CancellationToken ct) =>
        _page.Locator(selector).First.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Hidden,
            Timeout = timeoutMs
        });

    public Task WaitForUrlAsync(string urlPattern, int timeoutMs, CancellationToken ct) =>
        _page.WaitForURLAsync(urlPattern, new PageWaitForURLOptions { Timeout = timeoutMs });

    public Task SetInputFilesAsync(string selector, string filePath, CancellationToken ct) =>
        _page.SetInputFilesAsync(selector, filePath);

    public async Task<string> ScreenshotAsync(string filePath, CancellationToken ct)
    {
        var dir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        await _page.ScreenshotAsync(new PageScreenshotOptions { Path = filePath, FullPage = true });
        return filePath;
    }

    private static WaitUntilState ParseWaitUntil(string? waitUntil) => waitUntil?.ToLowerInvariant() switch
    {
        "load" => WaitUntilState.Load,
        "domcontentloaded" => WaitUntilState.DOMContentLoaded,
        "commit" => WaitUntilState.Commit,
        _ => WaitUntilState.NetworkIdle
    };
}
