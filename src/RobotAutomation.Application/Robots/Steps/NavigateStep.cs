using Microsoft.Extensions.Logging;
using RobotAutomation.Application.Robots.Abstractions;

namespace RobotAutomation.Application.Robots.Steps;

/// <summary>
/// Opens the portal and waits for the login form to render. Replaces the legacy
/// "navigate + Thread.Sleep(2000) + check URL" with an explicit DOM wait.
/// </summary>
public sealed class NavigateStep : IRobotStep
{
    public string Name => "Ouverture du portail";

    public async Task ExecuteAsync(RobotContext ctx, CancellationToken ct)
    {
        var url = ctx.Portal.FullUrl;
        ctx.Logger.LogInformation("Navigating to portal {Url}", url);
        await ctx.Page.GotoAsync(url, ctx.Portal.NavigationWaitUntil, ct);

        // Wait for whatever proves the landing page is ready: an explicit ReadySelector when set
        // (non-login portals), otherwise the login username field.
        var ready = string.IsNullOrWhiteSpace(ctx.Portal.ReadySelector)
            ? ctx.Portal.Selectors.UsernameInput
            : ctx.Portal.ReadySelector;
        await ctx.Page.WaitForSelectorAsync(ready, ctx.DefaultTimeoutMs, ct);
    }
}
