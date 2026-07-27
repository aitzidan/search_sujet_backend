using RobotAutomation.Application.Robots.Abstractions;
using RobotAutomation.Application.Robots.Steps;

namespace RobotAutomation.Application.Robots;

/// <summary>
/// Robot 3 — automates booking an appointment on the REAL DGI rendez-vous portal
/// (https://tax.rdv.gov.ma): open the wizard → choose prestation → choose date/slot →
/// fill applicant details → confirm → capture the confirmation.
///
/// Runs against the "rdv" portal config (real site), which ships with StopBeforeFinalSubmit = true
/// so it fills everything but does NOT book until you deliberately turn the dry-run off.
/// No login step: the rendez-vous portal is public (no authentication).
/// </summary>
public sealed class DgiRendezVousRobot : RobotBase
{
    private readonly NavigateStep _navigate;
    private readonly OpenRendezVousStep _openRendezVous;
    private readonly SelectPrestationStep _selectPrestation;
    private readonly ChooseSlotStep _chooseSlot;
    private readonly FillValidationStep _fillValidation;
    private readonly ConfirmRendezVousStep _confirm;
    private readonly CaptureConfirmationStep _capture;

    public DgiRendezVousRobot(
        NavigateStep navigate,
        OpenRendezVousStep openRendezVous,
        SelectPrestationStep selectPrestation,
        ChooseSlotStep chooseSlot,
        FillValidationStep fillValidation,
        ConfirmRendezVousStep confirm,
        CaptureConfirmationStep capture)
    {
        _navigate = navigate;
        _openRendezVous = openRendezVous;
        _selectPrestation = selectPrestation;
        _chooseSlot = chooseSlot;
        _fillValidation = fillValidation;
        _confirm = confirm;
        _capture = capture;
    }

    public override string Key => "dgi-rendezvous";

    public override string DisplayName => "DGI — Prise de rendez-vous (site réel)";

    protected override IEnumerable<IRobotStep> BuildSteps() =>
    [
        _navigate, _openRendezVous, _selectPrestation, _chooseSlot, _fillValidation, _confirm, _capture
    ];
}
