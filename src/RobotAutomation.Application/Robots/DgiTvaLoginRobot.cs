using RobotAutomation.Application.Robots.Abstractions;
using RobotAutomation.Application.Robots.Steps;

namespace RobotAutomation.Application.Robots;

/// <summary>
/// Robot 4 — opens the REAL TVA portal (tva.tax.gov.ma) and lets the OPERATOR authenticate.
///
/// The portal is protected by an image CAPTCHA and then by a 6-digit code e-mailed to the account
/// holder. Neither is automated here, by design: the robot opens a visible browser, waits while the
/// user types identifier, password and CAPTCHA, waits again for the e-mailed code, and only then takes
/// over. Consequences worth knowing:
/// <list type="bullet">
/// <item>no credentials are stored anywhere — the user types them into the portal itself;</item>
/// <item>the run holds a browser window open while it waits, so <c>Playwright:Headless</c> must be
/// false and <c>RunTimeoutMs</c> must exceed the operator's two waits.</item>
/// </list>
/// Automated alternatives still exist behind <c>DgiPortalOptions.CaptchaMode</c> (<see cref="Steps.ConnectWithCaptchaStep"/>
/// with the OCR solver) if this is ever revisited; they are simply not wired into this robot.
/// </summary>  
public sealed class DgiTvaLoginRobot : RobotBase
{
    private readonly NavigateStep _navigate;
    private readonly AwaitManualLoginStep _awaitLogin;
    private readonly AwaitOneTimeCodeStep _awaitCode;

    public DgiTvaLoginRobot(
        NavigateStep navigate,
        AwaitManualLoginStep awaitLogin,
        AwaitOneTimeCodeStep awaitCode)
    {
        _navigate = navigate;
        _awaitLogin = awaitLogin;
        _awaitCode = awaitCode;
    }

    public override string Key => "dgi-tva-login";

    public override string DisplayName => "TVA — Connexion (saisie par l'utilisateur)";

    protected override IEnumerable<IRobotStep> BuildSteps() =>
        [_navigate, _awaitLogin, _awaitCode];
}
