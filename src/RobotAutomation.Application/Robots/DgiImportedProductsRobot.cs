using RobotAutomation.Application.Robots.Abstractions;
using RobotAutomation.Application.Robots.Steps;

namespace RobotAutomation.Application.Robots;

/// <summary>
/// Robot 2 — proves automation beyond the login: after authenticating, open the "Produits importés"
/// screen and enter several product rows, then validate. Reuses the shared login steps, then the
/// imported-products steps.
/// </summary>
public sealed class DgiImportedProductsRobot : RobotBase
{
    private readonly NavigateStep _navigate;
    private readonly FillCredentialsStep _fillCredentials;
    private readonly SolveCaptchaStep _solveCaptcha;
    private readonly SubmitLoginStep _submitLogin;
    private readonly VerifySuccessStep _verifyLogin;
    private readonly OpenImportedProductsStep _openProducts;
    private readonly EnterImportedProductsStep _enterProducts;
    private readonly SubmitImportedProductsStep _submitProducts;
    private readonly VerifyImportedProductsStep _verifyProducts;

    public DgiImportedProductsRobot(
        NavigateStep navigate,
        FillCredentialsStep fillCredentials,
        SolveCaptchaStep solveCaptcha,
        SubmitLoginStep submitLogin,
        VerifySuccessStep verifyLogin,
        OpenImportedProductsStep openProducts,
        EnterImportedProductsStep enterProducts,
        SubmitImportedProductsStep submitProducts,
        VerifyImportedProductsStep verifyProducts)
    {
        _navigate = navigate;
        _fillCredentials = fillCredentials;
        _solveCaptcha = solveCaptcha;
        _submitLogin = submitLogin;
        _verifyLogin = verifyLogin;
        _openProducts = openProducts;
        _enterProducts = enterProducts;
        _submitProducts = submitProducts;
        _verifyProducts = verifyProducts;
    }

    public override string Key => "dgi-imported-products";

    public override string DisplayName => "DGI — Saisie des produits importés";

    protected override IEnumerable<IRobotStep> BuildSteps() =>
    [
        _navigate, _fillCredentials, _solveCaptcha, _submitLogin, _verifyLogin,
        _openProducts, _enterProducts, _submitProducts, _verifyProducts
    ];
}
