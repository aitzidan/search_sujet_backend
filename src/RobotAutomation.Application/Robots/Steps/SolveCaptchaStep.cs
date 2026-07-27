using Microsoft.Extensions.Logging;
using RobotAutomation.Application.Captcha;
using RobotAutomation.Application.Robots.Abstractions;

namespace RobotAutomation.Application.Robots.Steps;

/// <summary>
/// Resolves the CAPTCHA via <see cref="ICaptchaSolver"/> and types it into the CAPTCHA input.
/// The solver is a seam: the PoC reads the fake portal's plain-text challenge; a real image/OTP
/// CAPTCHA would get a different solver with no change to this step.
/// </summary>
public sealed class SolveCaptchaStep : IRobotStep
{
    private readonly ICaptchaSolver _captchaSolver;

    public SolveCaptchaStep(ICaptchaSolver captchaSolver) => _captchaSolver = captchaSolver;

    public string Name => "Résolution du CAPTCHA";

    public async Task ExecuteAsync(RobotContext ctx, CancellationToken ct)
    {
        var code = await _captchaSolver.SolveAsync(ctx, ct);
        ctx.Items["captcha"] = code;
        ctx.Logger.LogInformation("Solved CAPTCHA challenge ({Length} chars)", code.Length);
        await ctx.Page.FillAsync(ctx.Portal.Selectors.CaptchaInput, code, ct);
    }
}
