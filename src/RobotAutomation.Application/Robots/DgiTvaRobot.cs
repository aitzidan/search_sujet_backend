using RobotAutomation.Application.Robots.Abstractions;
using RobotAutomation.Application.Robots.Steps;

namespace RobotAutomation.Application.Robots;

/// <summary>
/// The TVA robot: it starts on the login page of the real portal (tva.tax.gov.ma), gets the user
/// authenticated, and continues straight into the VAT declaration work.
///
/// <para><b>Authentication is handed to the operator, by design.</b> The portal is guarded by an image
/// CAPTCHA and then by a 6-digit code e-mailed to the account holder. Neither is automated: the robot
/// opens a visible browser, waits while the user types identifier, password and CAPTCHA, waits again for
/// the e-mailed code, and only then takes over — so no credentials are stored anywhere.</para>
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
    /// <para>The rest is the portal's own dependency chain: the declaration must exist before its EDI
    /// archive can be attributed to it, the archive must be sent before a recalculation has anything to
    /// consolidate, and both the amounts and the recalculation happen in the declaration's own edit page —
    /// which is why the robot leaves the EDI section and comes back through the list. The amounts are typed
    /// there, then « Calculer » derives every total and every tax the portal computes itself. Filing it
    /// ("Soumettre à validation") is deliberately NOT here: that is the irreversible act, and it stays the
    /// operator's.</para>
    /// </summary>
    protected override IEnumerable<IRobotStep> BuildSteps() =>
    [
        _loadData, _open, _awaitLogin, _awaitCode,
        _deleteExisting, _openDeclaration, _create, _save,
        _openEdi, _sendEdi, _backToList, _edit, _fill, _recalculate
    ];
}
