using RobotAutomation.Application.Captcha;
using RobotAutomation.Application.Robots.Abstractions;

namespace RobotAutomation.Infrastructure.Captcha;

/// <summary>
/// The single <see cref="ICaptchaSolver"/> registered in DI; picks the real implementation per run
/// from <c>DgiPortalOptions.CaptchaMode</c> — so different portals (fake test portal vs. a real image
/// CAPTCHA) can each get the solver that actually fits them without any step needing to know which.
/// </summary>
internal sealed class CaptchaSolverDispatcher : ICaptchaSolver
{
    private readonly DomTextCaptchaSolver _domText;
    private readonly ManualCaptchaSolver _manual;
    private readonly OcrCaptchaSolver _ocr;

    public CaptchaSolverDispatcher(DomTextCaptchaSolver domText, ManualCaptchaSolver manual, OcrCaptchaSolver ocr)
    {
        _domText = domText;
        _manual = manual;
        _ocr = ocr;
    }

    public Task<string> SolveAsync(RobotContext ctx, CancellationToken ct) => ctx.Portal.CaptchaMode switch
    {
        var mode when string.Equals(mode, "Manual", StringComparison.OrdinalIgnoreCase) => _manual.SolveAsync(ctx, ct),
        var mode when string.Equals(mode, "Ocr", StringComparison.OrdinalIgnoreCase) => _ocr.SolveAsync(ctx, ct),
        _ => _domText.SolveAsync(ctx, ct),
    };
}
