using Microsoft.Extensions.Logging;
using RobotAutomation.Application.Robots.Abstractions;

namespace RobotAutomation.Application.Robots.Steps;

/// <summary>
/// Clicks the login submit button. Marked non-retryable so Polly never double-submits —
/// the pattern that must carry over to the real DGI's "submit for validation".
/// </summary>
public sealed class SubmitLoginStep : IRobotStep
{
    public string Name => "Soumission du formulaire";

    public bool Retryable => false;

    public async Task ExecuteAsync(RobotContext ctx, CancellationToken ct)
    {
        // Re-check every required field is still populated right before the irreversible submit —
        // reselects/refills anything that came back empty instead of trusting the earlier fill blindly.
        await EnsureFieldFilledAsync(ctx, ctx.Portal.Selectors.UsernameInput, LoginCredentials.Username(ctx), ct);
        await EnsureFieldFilledAsync(ctx, ctx.Portal.Selectors.PasswordInput, LoginCredentials.Password(ctx), ct);
        if (ctx.Items.TryGetValue("captcha", out var captchaObj) && captchaObj is string { Length: > 0 } captcha)
            await EnsureFieldFilledAsync(ctx, ctx.Portal.Selectors.CaptchaInput, captcha, ct);

        ctx.Logger.LogInformation("Submitting login form");
        await ctx.Page.ClickAsync(ctx.Portal.Selectors.SubmitButton, ct);
    }

    private static async Task EnsureFieldFilledAsync(RobotContext ctx, string selector, string expected, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(selector) || string.IsNullOrEmpty(expected)) return;
        var current = await ctx.Page.GetValueAsync(selector, ct);
        if (string.IsNullOrEmpty(current))
            await ctx.Page.FillAsync(selector, expected, ct);
    }
}
