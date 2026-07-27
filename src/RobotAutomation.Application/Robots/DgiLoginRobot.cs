using RobotAutomation.Application.Robots.Abstractions;
using RobotAutomation.Application.Robots.Steps;

namespace RobotAutomation.Application.Robots;

/// <summary>
/// The PoC login robot: open portal → fill credentials → solve CAPTCHA → submit → verify.
/// This single definition is launched as many independent, concurrent runs (each with its own
/// credentials and isolated browser context) — the modern form of the legacy per-société loop.
///
/// A future scenario (e.g. a full declaration flow mirroring the legacy Etape order) is just
/// another <see cref="RobotBase"/> subclass reusing these steps plus new ones — no engine change.
/// </summary>
public sealed class DgiLoginRobot : RobotBase
{
    private readonly NavigateStep _navigate;
    private readonly FillCredentialsStep _fillCredentials;
    private readonly SolveCaptchaStep _solveCaptcha;
    private readonly SubmitLoginStep _submit;
    private readonly VerifySuccessStep _verify;

    public DgiLoginRobot(
        NavigateStep navigate,
        FillCredentialsStep fillCredentials,
        SolveCaptchaStep solveCaptcha,
        SubmitLoginStep submit,
        VerifySuccessStep verify)
    {
        _navigate = navigate;
        _fillCredentials = fillCredentials;
        _solveCaptcha = solveCaptcha;
        _submit = submit;
        _verify = verify;
    }

    public override string Key => "dgi-login";

    public override string DisplayName => "DGI — Connexion (portail de test)";

    protected override IEnumerable<IRobotStep> BuildSteps() =>
        [_navigate, _fillCredentials, _solveCaptcha, _submit, _verify];
}
