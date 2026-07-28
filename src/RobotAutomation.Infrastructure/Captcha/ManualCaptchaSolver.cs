using Microsoft.Extensions.Logging;
using RobotAutomation.Application.Captcha;
using RobotAutomation.Application.Robots.Abstractions;

namespace RobotAutomation.Infrastructure.Captcha;

/// <summary>
/// CAPTCHA solver for a real image challenge: no OCR/bypass is attempted. The robot (running
/// non-headless) waits for a human to look at the visible browser window and type the answer
/// into the CAPTCHA input themselves — the same hand-off the legacy WPF robot always relied on
/// for the real DGI site. Deliberately given a much longer timeout than ordinary DOM waits, since
/// it is waiting on a person, not the page.
/// </summary>
internal sealed class ManualCaptchaSolver : ICaptchaSolver
{
    private const int ManualEntryTimeoutMs = 240_000; // 4 minutes

    public async Task<string> SolveAsync(RobotContext ctx, CancellationToken ct)
    {
        var challenge = ctx.Portal.Selectors.CaptchaChallenge;
        if (!string.IsNullOrWhiteSpace(challenge))
            await ctx.Page.WaitForSelectorAsync(challenge, ctx.DefaultTimeoutMs, ct);

        ctx.Logger.LogWarning(
            "CAPTCHA : veuillez saisir le code affiché dans la fenêtre du navigateur — en attente de votre saisie ({TimeoutMs} ms max)...",
            ManualEntryTimeoutMs);

        return await ctx.Page.WaitForNonEmptyValueAsync(ctx.Portal.Selectors.CaptchaInput, ManualEntryTimeoutMs, ct);
    }
}
