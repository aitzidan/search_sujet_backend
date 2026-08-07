using RobotAutomation.Application.Robots.Abstractions;
using RobotAutomation.Application.Robots.Steps;

namespace RobotAutomation.Application.Robots;

/// <summary>
/// The TVA robot — one robot for the whole flow on the REAL portal (tva.tax.gov.ma): it starts on the
/// login page, gets the user authenticated, and continues straight into the VAT declaration work.
/// Authentication is a step of this robot, not a separate robot.
///
/// <para><b>Authentication is handed to the operator, by design.</b> The portal is guarded by an image
/// CAPTCHA and then by a 6-digit code e-mailed to the account holder. Neither is automated: the robot
/// opens a visible browser, waits while the user types identifier, password and CAPTCHA, waits again for
/// the e-mailed code, and only then takes over. Consequences worth knowing:</para>
/// <list type="bullet">
/// <item>no credentials are stored anywhere — the user types them into the portal itself;</item>
/// <item>the run holds a browser window open while it waits, so <c>Playwright:Headless</c> must be false
/// and <c>RunTimeoutMs</c> must exceed the operator's two waits;</item>
/// <item>with <c>DgiPortals:real:ReuseSession</c> on, both waits skip themselves when the run inherits a
/// still-valid session, so a development loop authenticates once and then goes straight to the
/// declaration work.</item>
/// </list>
///
/// <para>Automated CAPTCHA solving still exists behind <c>DgiPortalOptions.CaptchaMode</c>
/// (<see cref="ConnectWithCaptchaStep"/> with the OCR solver) if this is ever revisited; it is simply not
/// wired in here.</para>
///
/// <para>The declaration part is built one step at a time, and the order is a dependency chain: the home
/// page is cleared of any pending declaration (the portal refuses to create a new one alongside it), then
/// the current-period section is opened, a declaration is created, and it is saved.</para>
/// </summary>
public sealed class DgiTvaRobot : RobotBase
{
    private readonly LoadDeclarationDataStep _loadData;
    private readonly OpenPortalStep _open;
    private readonly AwaitManualLoginStep _awaitLogin;
    private readonly AwaitOneTimeCodeStep _awaitCode;
    private readonly DeleteExistingDeclarationStep _deleteExisting;
    private readonly OpenCurrentPeriodDeclarationStep _openDeclaration;
    private readonly CreateDeclarationStep _create;
    private readonly SaveDeclarationStep _save;
    private readonly OpenEdiUploadStep _openEdi;
    private readonly SendEdiFileStep _sendEdi;
    private readonly ReturnToDeclarationListStep _backToList;
    private readonly EditDeclarationStep _edit;
    private readonly FillDeclarationAmountsStep _fill;
    private readonly RecalculateDeclarationStep _recalculate;

    public DgiTvaRobot(
        LoadDeclarationDataStep loadData,
        OpenPortalStep open,
        AwaitManualLoginStep awaitLogin,
        AwaitOneTimeCodeStep awaitCode,
        DeleteExistingDeclarationStep deleteExisting,
        OpenCurrentPeriodDeclarationStep openDeclaration,
        CreateDeclarationStep create,
        SaveDeclarationStep save,
        OpenEdiUploadStep openEdi,
        SendEdiFileStep sendEdi,
        ReturnToDeclarationListStep backToList,
        EditDeclarationStep edit,
        FillDeclarationAmountsStep fill,
        RecalculateDeclarationStep recalculate)
    {
        _loadData = loadData;
        _open = open;
        _awaitLogin = awaitLogin;
        _awaitCode = awaitCode;
        _deleteExisting = deleteExisting;
        _openDeclaration = openDeclaration;
        _create = create;
        _save = save;
        _openEdi = openEdi;
        _sendEdi = sendEdi;
        _backToList = backToList;
        _edit = edit;
        _fill = fill;
        _recalculate = recalculate;
    }

    public override string Key => "dgi-tva";

    public override string DisplayName => "TVA — Connexion et déclaration";

    /// <summary>
    /// Data first, deliberately: loading the figures needs no browser, so a bad dossier path fails in
    /// seconds rather than after the operator has completed a manual login.
    ///
    /// <para>Everything after the save is a dependency chain too, and the order is the portal's, not a
    /// preference: the declaration must exist before its EDI archive can be attributed to it, the archive
    /// must be sent before a recalculation has anything to consolidate, and both the amounts and the
    /// recalculation happen in the declaration's own edit page — which is why the robot leaves the EDI
    /// section and comes back through the list. The amounts are typed there, then « Calculer » derives every
    /// total and every tax the portal computes itself. Filing it ("Soumettre à validation") is deliberately
    /// NOT here: that is the irreversible act, and it stays the operator's.</para>
    /// </summary>
    protected override IEnumerable<IRobotStep> BuildSteps() =>
    [
        _loadData, _open, _awaitLogin, _awaitCode,
        _deleteExisting, _openDeclaration, _create, _save,
        _openEdi, _sendEdi, _backToList, _edit, _fill, _recalculate
    ];
}
