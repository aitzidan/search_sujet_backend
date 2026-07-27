using RobotAutomation.Application.Robots.Abstractions;
using RobotAutomation.Application.Robots.Steps;

namespace RobotAutomation.Application.Robots;

/// <summary>
/// Robot 1 — reproduces the legacy télédéclaration flow against the (fake) DGI portal:
/// login → open menu → create period → upload EDI → fill the TVA recap → submit → confirm.
/// Reuses the shared login steps, then the declaration-specific steps.
/// </summary>
public sealed class DgiDeclarationRobot : RobotBase
{
    private readonly NavigateStep _navigate;
    private readonly FillCredentialsStep _fillCredentials;
    private readonly SolveCaptchaStep _solveCaptcha;
    private readonly SubmitLoginStep _submitLogin;
    private readonly VerifySuccessStep _verifyLogin;
    private readonly OpenDeclarationStep _openDeclaration;
    private readonly CreatePeriodStep _createPeriod;
    private readonly UploadEdiFileStep _uploadEdi;
    private readonly FillDeclarationStep _fillDeclaration;
    private readonly SubmitDeclarationStep _submitDeclaration;
    private readonly VerifyDeclarationStep _verifyDeclaration;

    public DgiDeclarationRobot(
        NavigateStep navigate,
        FillCredentialsStep fillCredentials,
        SolveCaptchaStep solveCaptcha,
        SubmitLoginStep submitLogin,
        VerifySuccessStep verifyLogin,
        OpenDeclarationStep openDeclaration,
        CreatePeriodStep createPeriod,
        UploadEdiFileStep uploadEdi,
        FillDeclarationStep fillDeclaration,
        SubmitDeclarationStep submitDeclaration,
        VerifyDeclarationStep verifyDeclaration)
    {
        _navigate = navigate;
        _fillCredentials = fillCredentials;
        _solveCaptcha = solveCaptcha;
        _submitLogin = submitLogin;
        _verifyLogin = verifyLogin;
        _openDeclaration = openDeclaration;
        _createPeriod = createPeriod;
        _uploadEdi = uploadEdi;
        _fillDeclaration = fillDeclaration;
        _submitDeclaration = submitDeclaration;
        _verifyDeclaration = verifyDeclaration;
    }

    public override string Key => "dgi-declaration";

    public override string DisplayName => "DGI — Télédéclaration TVA";

    protected override IEnumerable<IRobotStep> BuildSteps() =>
    [
        _navigate, _fillCredentials, _solveCaptcha, _submitLogin, _verifyLogin,
        _openDeclaration, _createPeriod, _uploadEdi, _fillDeclaration, _submitDeclaration, _verifyDeclaration
    ];
}
