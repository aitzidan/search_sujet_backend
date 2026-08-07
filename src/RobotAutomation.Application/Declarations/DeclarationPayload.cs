namespace RobotAutomation.Application.Declarations;

/// <summary>
/// Everything the robot needs to fill a VAT declaration, as computed by the legacy GénéraFi business
/// layer from one client's accounting dossier.
///
/// Mirrors the JSON contract of <c>GeneraFi.Robot.Bridge.exe declaration-payload</c> (see
/// <c>generafi_tva/GeneraFi.Robot.Bridge/Contracts.cs</c>) — the two are one contract across a process
/// boundary and must change together.
/// </summary>
public sealed record DeclarationPayload(
    SocieteInfo Societe,
    PeriodeInfo Periode,
    IReadOnlyList<DeclarationLine> Lignes,
    SuiviInfo? Suivi,
    IReadOnlyList<EdiFile> EdiFiles)
{
    /// <summary>Lines carrying an actual amount — what the robot will actually type. The full list also
    /// holds every zero line, which the portal may need cleared rather than skipped.</summary>
    public IEnumerable<DeclarationLine> NonZeroLignes =>
        Lignes.Where(l => l.MntG != 0 || l.MntD != 0);
}

public sealed record SocieteInfo(
    string? FiscalId,
    string? Nom,
    string? RaisonSociale,
    string? Ice,
    /// <summary>"Mensuel" or "Trimestriel" — the régime to select when creating the declaration.</summary>
    string? Mode,
    /// <summary>"Débit" or "Encaissement".</summary>
    string? Regime,
    /// <summary>The portal's fait générateur: "D" for Débit, "E" for Encaissement.</summary>
    string? FaitGenerateur,
    bool Consolide,
    bool ConsolidantMere);

public sealed record PeriodeInfo(
    int Id,
    int Annee,
    int Mois,
    double Prorata,
    string? Du,
    string? Au,
    string? Nom);

/// <summary>
/// One line of the declaration. <see cref="Code"/> is the addressing key — the portal renders it as the
/// first cell of the line's row, and it is stable across the portal redesigns that invalidated the
/// legacy robot's positional coordinates.
/// </summary>
public sealed record DeclarationLine(
    string Code,
    string? Libelle,
    /// <summary>Which block of the declaration the line belongs to ("bgA".."bgE").</summary>
    string? Contenant,
    /// <summary>Left column — base or turnover.</summary>
    double MntG,
    /// <summary>Right column — tax.</summary>
    double MntD,
    double TauxTva,
    /// <summary>Diagnostic only. Legacy "table row tdG tdD" coordinates from the per-year Codification
    /// table; they go stale whenever the portal is redesigned. Never address a field with this.</summary>
    string? Rank);

/// <summary>The legacy télédéclaration progress for this période, shared with the desktop app.</summary>
public sealed record SuiviInfo(
    string? Etape,
    int PeriodeId,
    string? NomFichier,
    string? SelectedIf,
    string? Commentaire);

/// <summary>
/// An EDI archive the bridge generated from the dossier, ready to upload on the portal's "Envoi EDI" page.
///
/// Identified by path, not carried as bytes: this side chose the directory in the first place
/// (<see cref="Configuration.BridgeOptions.ResolveEdiDirectory"/>) and the bridge runs on this machine — so
/// the split is that robot-automation decides *where* and the bridge decides *what*.
/// </summary>
public sealed record EdiFile(
    /// <summary>"Main", "NR" or "RAS" — the portal uploads each through a different flow. Only "Main" is
    /// produced today.</summary>
    string Kind,
    string Name,
    string MimeType,
    string Path,
    /// <summary>Set when the archive was produced but is worth a second look — currently only "no deductions
    /// at all", legitimate for a période with no purchases and also what a misread dossier looks like.</summary>
    string? Warning);
