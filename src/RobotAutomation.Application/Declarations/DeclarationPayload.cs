namespace RobotAutomation.Application.Declarations;

/// <summary>
/// Everything the robot needs to fill a VAT declaration, as computed by the legacy GénéraFi business
/// layer from one client's accounting dossier.
/// </summary>
public sealed record DeclarationPayload(
    SocieteInfo Societe,
    PeriodeInfo Periode,
    IReadOnlyList<DeclarationLine> Lignes,
    SuiviInfo? Suivi,
    IReadOnlyList<EdiFile> EdiFiles)
{
    /// <summary>Lines carrying an actual amount — what the robot will type.</summary>
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

public sealed record DeclarationLine(
    /// <summary>The DGI line code, printed in the first cell of the line's row on the portal.</summary>
    string Code,
    string? Libelle,
    /// <summary>Which block of the declaration the line belongs to ("bgA".."bgE").</summary>
    string? Contenant,
    /// <summary>Left column — base or turnover.</summary>
    double MntG,
    /// <summary>Right column — tax.</summary>
    double MntD,
    double TauxTva,
    string? Rank);

/// <summary>The legacy télédéclaration progress for this période, shared with the desktop app.</summary>
public sealed record SuiviInfo(
    string? Etape,
    int PeriodeId,
    string? NomFichier,
    string? SelectedIf,
    string? Commentaire);

/// <summary>An EDI archive generated from the dossier, ready to upload on the portal's "Envoi EDI" page.</summary>
public sealed record EdiFile(
    /// <summary>"Main" (déductions), "NR" (non-residents) or "RAS" (retenue à la source).</summary>
    string Kind,
    string Name,
    string MimeType,
    string Path,
    /// <summary>Set when the archive was produced but is worth a second look — currently only "no
    /// deductions at all", legitimate for a période with no purchases and also what a misread dossier
    /// looks like.</summary>
    string? Warning);
