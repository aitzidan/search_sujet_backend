using RobotAutomation.Application.Robots.Abstractions;

namespace RobotAutomation.Application.Captcha;

/// <summary>
/// Resolves the CAPTCHA challenge into the string to type into the CAPTCHA input.
///
/// The PoC implementation (<c>DomTextCaptchaSolver</c>) simply reads the challenge code that the
/// fake portal renders as visible text. The seam is preserved so a real image/OTP CAPTCHA later
/// gets a new implementation (OCR, an AI solver, or an operator hand-off) with no change to
/// <c>SolveCaptchaStep</c>.
/// </summary>
public interface ICaptchaSolver
{
    Task<string> SolveAsync(RobotContext ctx, CancellationToken ct);
}
