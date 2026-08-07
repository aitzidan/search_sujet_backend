using Microsoft.Playwright;
using RobotAutomation.Application.Robots;

namespace RobotAutomation.Infrastructure.Playwright;

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
                    const isPlaceholder = o => o.disabled || !o.value || o.value === '0' || o.value === '-1'
                        || /^(s[ée]lectionnez|choisir|choisissez)/.test(norm(o.textContent));
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

        try
        {
            await _page.EvaluateAsync(
                @"(sel) => {
                    const el = document.querySelector(sel);
                    if (el && window.jQuery) { try { window.jQuery(el).trigger('chosen:updated'); } catch (e) {} }
                }",
                selector);
        }
        catch
        {
        }
    }

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

    public async Task<bool> IsDisabledAsync(string selector, CancellationToken ct)
    {
        var locator = _page.Locator(selector).First;
        if (await locator.CountAsync() == 0) return false;
        return await locator.IsDisabledAsync();
    }

    public async Task<bool> IsEditableAsync(string selector, CancellationToken ct)
    {
        var locator = _page.Locator(selector).First;
        if (await locator.CountAsync() == 0) return false;
        return await locator.IsEditableAsync();
    }

    public async Task<string?> GetValueAsync(string selector, CancellationToken ct)
    {
        var locator = _page.Locator(selector).First;
        if (await locator.CountAsync() == 0) return null;
        return await locator.InputValueAsync();
    }

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

    public async Task<bool> IsInViewportAsync(string selector, CancellationToken ct)
    {
        var locator = _page.Locator(selector).First;
        if (await locator.CountAsync() == 0) return false;

        return await locator.EvaluateAsync<bool>(
            @"el => {
                const r = el.getBoundingClientRect();
                if (r.width <= 0 || r.height <= 0) return false;
                const w = window.innerWidth || document.documentElement.clientWidth;
                const h = window.innerHeight || document.documentElement.clientHeight;
                return r.bottom > 0 && r.right > 0 && r.top < h && r.left < w;
            }");
    }

    public Task ScrollToTopAsync(CancellationToken ct) =>
        _page.EvaluateAsync("() => window.scrollTo(0, 0)");

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
