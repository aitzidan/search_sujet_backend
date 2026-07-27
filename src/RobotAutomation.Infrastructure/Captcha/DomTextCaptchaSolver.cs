using RobotAutomation.Application.Captcha;
using RobotAutomation.Application.Robots.Abstractions;

namespace RobotAutomation.Infrastructure.Captcha;

/// <summary>
/// PoC CAPTCHA solver: reads the challenge code the fake portal renders as visible text
/// (deterministic, no OCR). A real image/OTP CAPTCHA would get a different implementation
/// behind the same <see cref="ICaptchaSolver"/> seam with no change to the robot step.
/// </summary>
internal sealed class DomTextCaptchaSolver : ICaptchaSolver
{
    public async Task<string> SolveAsync(RobotContext ctx, CancellationToken ct)
    {
        var selector = ctx.Portal.Selectors.CaptchaChallenge;
        if (string.IsNullOrWhiteSpace(selector))
            return string.Empty; // portal has no CAPTCHA configured

        await ctx.Page.WaitForSelectorAsync(selector, ctx.DefaultTimeoutMs, ct);
        var challenge = await ctx.Page.GetTextAsync(selector, ct);
        return (challenge ?? string.Empty).Trim();
    }
}
