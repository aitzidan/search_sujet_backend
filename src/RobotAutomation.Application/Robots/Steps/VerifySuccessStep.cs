using Microsoft.Extensions.Logging;
using RobotAutomation.Application.Robots.Abstractions;

namespace RobotAutomation.Application.Robots.Steps;

/// <summary>
/// Confirms the login succeeded, driven entirely by <c>DgiPortalOptions.SuccessRule</c>:
/// <list type="bullet">
/// <item><b>SelectorVisible</b> — a success-page element is visible (the fake portal).</item>
/// <item><b>PopupTitleContains</b> — a SweetAlert2-style popup whose title contains the success
/// text; otherwise the popup content is the error. This is the legacy DGI behaviour, now data-driven.</item>
/// </list>
/// Throws on failure so the executor records the step as Failed (with a screenshot) and stops.
/// </summary>
public sealed class VerifySuccessStep : IRobotStep
{
    public string Name => "Vérification de la connexion";

    public async Task ExecuteAsync(RobotContext ctx, CancellationToken ct)
    {
        var rule = ctx.Portal.SuccessRule;
        var mode = rule.Mode?.Trim();

        if (string.Equals(mode, "PopupTitleContains", StringComparison.OrdinalIgnoreCase))
        {
            await VerifyPopupAsync(ctx, ct);
            return;
        }

        // Default: SelectorVisible
        var indicator = ctx.Portal.Selectors.SuccessIndicator;
        await ctx.Page.WaitForSelectorAsync(indicator, ctx.DefaultTimeoutMs, ct);
        if (!await ctx.Page.IsVisibleAsync(indicator, ct))
        {
            throw new InvalidOperationException(
                $"Success indicator '{indicator}' did not appear — login was not confirmed.");
        }

        ctx.Logger.LogInformation("Login confirmed via success indicator {Indicator}", indicator);
    }

    private static async Task VerifyPopupAsync(RobotContext ctx, CancellationToken ct)
    {
        var rule = ctx.Portal.SuccessRule;
        var titleSelector = rule.TitleSelector
            ?? throw new InvalidOperationException("SuccessRule.TitleSelector is required for PopupTitleContains mode.");

        await ctx.Page.WaitForSelectorAsync(titleSelector, ctx.DefaultTimeoutMs, ct);
        var title = await ctx.Page.GetTextAsync(titleSelector, ct) ?? "";
        var succeeded = !string.IsNullOrEmpty(rule.SuccessText)
                        && title.Contains(rule.SuccessText, StringComparison.OrdinalIgnoreCase);

        // Dismiss the popup (mirrors clicking .swal2-actions button) regardless of outcome.
        if (!string.IsNullOrWhiteSpace(rule.DismissSelector) && await ctx.Page.IsVisibleAsync(rule.DismissSelector!, ct))
        {
            await ctx.Page.ClickAsync(rule.DismissSelector!, ct);
        }

        if (succeeded)
        {
            ctx.Logger.LogInformation("Login confirmed via popup title '{Title}'", title);
            return;
        }

        var error = rule.ContentSelector is not null
            ? await ctx.Page.GetTextAsync(rule.ContentSelector, ct)
            : null;
        throw new InvalidOperationException(
            $"Login failed. Popup title: '{title}'. Detail: {error ?? "(none)"}");
    }
}
