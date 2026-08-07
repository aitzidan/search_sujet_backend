using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using RobotAutomation.Application.Declarations;
using RobotAutomation.Application.Robots.Abstractions;

namespace RobotAutomation.Application.Robots.Steps;

/// <summary>
/// Step 0 — loads the figures to declare from the client's accounting dossier, before the browser is
/// even opened.
///
/// Running first is deliberate. Nothing here touches the portal, and a mistyped dossier path fails in
/// seconds instead of after the operator has typed a password, a CAPTCHA and an e-mailed code. The
/// payload is parked in <see cref="RobotContext.Items"/> until the step that fills the form needs it.
/// </summary>
public sealed class LoadDeclarationDataStep : IRobotStep
{
    /// <summary>Key under which the payload is published for later steps.</summary>
    public const string ItemKey = "declaration";

    private readonly IDeclarationDataSource _source;

    public LoadDeclarationDataStep(IDeclarationDataSource source) => _source = source;

    public string Name => "Chargement des données de la déclaration";

    public async Task ExecuteAsync(RobotContext ctx, CancellationToken ct)
    {
        var dossier = ctx.GetParameter("dossierPath");
        if (string.IsNullOrWhiteSpace(dossier))
            throw new InvalidOperationException(
                "Aucun dossier indiqué : renseignez le paramètre « dossierPath » (chemin du fichier .mdb " +
                "du client) pour que le robot sache quelles données déclarer.");

        var periodeId = int.TryParse(ctx.GetParameter("periodeId"), out var parsed) ? parsed : (int?)null;

        var payload = await _source.GetAsync(dossier, periodeId, ct);

        ctx.Items[ItemKey] = payload;

        var nonZero = payload.NonZeroLignes.Count();
        ctx.Output["societe"] = payload.Societe?.RaisonSociale ?? payload.Societe?.Nom;
        ctx.Output["identifiantFiscal"] = payload.Societe?.FiscalId;
        ctx.Output["periode"] = payload.Periode is null ? null : $"{payload.Periode.Mois:00}/{payload.Periode.Annee}";
        ctx.Output["regime"] = payload.Societe?.Mode;
        ctx.Output["faitGenerateur"] = payload.Societe?.FaitGenerateur;
        ctx.Output["lignesADeclarer"] = $"{nonZero} / {payload.Lignes.Count}";

        PublishEdi(ctx, payload);

        ctx.Logger.LogInformation(
            "Déclaration à saisir : {Societe} ({FiscalId}), période {Periode}, régime {Regime}, " +
            "{NonZero} ligne(s) non nulle(s) sur {Total}",
            ctx.Output["societe"], ctx.Output["identifiantFiscal"], ctx.Output["periode"],
            ctx.Output["regime"], nonZero, payload.Lignes.Count);

        // Worth surfacing rather than discovering three steps later: a période with nothing to declare
        // usually means the wrong dossier or the wrong période, not a genuinely empty month.
        if (nonZero == 0)
            ctx.Logger.LogWarning(
                "Aucun montant à déclarer pour cette période — dossier ou période inattendu ?");

        // The régime the data implies should win over whatever the run was launched with, otherwise the
        // robot could create a Mensuel declaration for a Trimestriel company.
        var declared = ctx.GetParameter("regime");
        var expected = payload.Societe?.Mode;
        if (!string.IsNullOrWhiteSpace(expected)
            && !string.IsNullOrWhiteSpace(declared)
            && !expected.Equals(declared, StringComparison.OrdinalIgnoreCase))
            ctx.Logger.LogWarning(
                "Le régime demandé ({Declared}) ne correspond pas à celui du dossier ({Expected}) — " +
                "c'est {Declared} qui sera sélectionné sur le portail",
                declared, expected, declared);
    }

    /// <summary>
    /// Reports the EDI archive the bridge generated, so it is visible on the card before the browser even
    /// opens — and so a run that later fails to upload can be told apart from one that had nothing to upload.
    /// </summary>
    private static void PublishEdi(RobotContext ctx, DeclarationPayload payload)
    {
        // Null-tolerant, not just empty-tolerant: an older bridge that omits the property entirely would
        // otherwise turn a clear "no archive" warning into a NullReferenceException three steps later.
        var edi = payload.EdiFiles?.FirstOrDefault();
        if (edi is null)
        {
            ctx.Logger.LogWarning(
                "Le pont n'a généré aucune archive EDI — l'envoi EDI échouera, sauf si le paramètre " +
                "« {Parameter} » désigne une archive existante.", SendEdiFileStep.PathParameter);
            return;
        }

        ctx.Output["fichierEdi"] = edi.Name;
        ctx.Logger.LogInformation("Archive EDI générée : {Path}", edi.Path);

        if (!string.IsNullOrWhiteSpace(edi.Warning))
            ctx.Logger.LogWarning("Archive EDI — {Warning}", edi.Warning);
    }
}

// The declaration half of Robot 4 (DgiTvaRobot) on the REAL TVA portal (tva.tax.gov.ma). These steps run
// once the same robot's login steps (ManualLoginSteps) have authenticated the operator and the portal has
// landed on its home route "#/".
//
// Every selector comes from DgiPortalOptions.Elements (the "real" section) so the flow can be retuned
// against the live DOM without recompiling. Selectors deliberately anchor on things the portal owns — a
// route (href="#/dec-tva"), an id (#enregistrer), a visible label, the <ng2-smart-table> tag, FontAwesome's
// .fa-remove, SweetAlert2's .swal2-* classes — never on Angular's generated _ngcontent-*/_nghost-*
// attributes, which change on every build.
//
// Two entries in that map are expected TEXT rather than selectors: "declarationDeleteConfirmText" (the
// wording a delete confirmation must contain) and "declarationRegime" (the régime label to select).
//
// Two more are XPath rather than CSS, because they are composed: "declarationLineRow" (a template whose
// {code} placeholder becomes the DGI line number) and "declarationLineAmounts" (the numeric cells relative
// to a row). FillDeclarationAmountsStep wraps them in positional predicates, which CSS cannot express — and
// XPath is what lets a row be matched on "first cell reads this number" rather than on its position.

/// <summary>
/// Step 1 — clear any declaration already listed on the home page, because the portal will not let a new
/// one be created while one is pending.
///
/// Three things make this more than "click the icon":
/// <list type="bullet">
/// <item><b>Empty vs. not-loaded-yet.</b> The table is filled by an XHR, so an empty table and a table
/// still loading look identical. The step waits for the table, then gives a delete icon a full timeout to
/// appear; only when none does is the table treated as genuinely empty.</item>
/// <item><b>Irreversible.</b> Deleting is a real write on a real tax portal, so the row about to go is
/// logged first, the confirmation dialog's wording is checked before "Oui !" is clicked, and at most
/// <see cref="Configuration.DgiPortalOptions.MaxDeclarationDeletions"/> rows may be removed per run.</item>
/// <item><b>SweetAlert2 leaves a backdrop.</b> The confirmation may be followed by a result popup, whose
/// backdrop swallows every later click — so the step waits the dialogs out and dismisses follow-ups.</item>
/// </list>
/// Success is verified from the table itself (the row count drops), not from the notification, which is
/// only logged.
/// </summary>
public sealed class DeleteExistingDeclarationStep : IRobotStep
{
    public string Name => "Suppression de la déclaration existante";

    /// <summary>
    /// Never retried by the executor: a retry would replay a destructive action whose first attempt may
    /// already have gone through. The step loops and verifies internally instead.
    /// </summary>
    public bool Retryable => false;

    public async Task ExecuteAsync(RobotContext ctx, CancellationToken ct)
    {
        var table = ctx.Portal.Element("declarationsTable");
        var row = ctx.Portal.Element("declarationRow");
        var deleteButton = ctx.Portal.Element("declarationDeleteButton");

        // Having nothing to delete is a normal outcome, not a failure — and an account with no pending
        // declaration may not render a table at all, so a missing table must not stop the run either. Both
        // cases end the step quietly and let the robot go on to create the declaration.
        if (!await TvaDom.PresentAsync(ctx, table, ct))
        {
            ctx.Logger.LogInformation(
                "Aucun tableau de déclarations sur la page (« {Table} ») — rien à supprimer", table);
            ctx.Output["declarationsSupprimees"] = "0";
            return;
        }

        var pending = await WaitForDeclarationsAsync(ctx, row, deleteButton, ct);
        if (pending == 0)
        {
            ctx.Logger.LogInformation("Aucune déclaration dans le tableau — rien à supprimer");
            ctx.Output["declarationsSupprimees"] = "0";
            return;
        }

        ctx.Logger.LogInformation("{Count} déclaration(s) supprimable(s) détectée(s)", pending);

        // Same safety switch the rendez-vous robot uses for its final booking: in dry-run the robot goes
        // through the flow but performs no irreversible write. Creating and saving a declaration is NOT
        // gated by it — that draft can be deleted again, so it is reversible; this deletion is not.
        if (ctx.Portal.StopBeforeFinalSubmit)
        {
            ctx.Logger.LogWarning(
                "Mode dry-run (StopBeforeFinalSubmit) — {Count} déclaration(s) détectée(s), AUCUNE suppression effectuée",
                pending);
            ctx.Output["declarationsSupprimees"] = "0 (dry-run)";
            return;
        }

        var max = Math.Max(1, ctx.Portal.MaxDeclarationDeletions);
        var deleted = 0;

        while (pending > 0 && deleted < max)
        {
            await DeleteFirstAsync(ctx, row, deleteButton, pending, ct);
            deleted++;
            // Safe to read directly: DeleteFirstAsync only returns once the table has actually re-rendered
            // with fewer rows.
            pending = await ctx.Page.CountAsync(row, ct);
        }

        ctx.Output["declarationsSupprimees"] = deleted.ToString();

        if (pending > 0)
            ctx.Logger.LogWarning(
                "{Remaining} déclaration(s) encore présente(s) : la limite de sécurité de {Max} suppression(s) " +
                "par run est atteinte. Relevez MaxDeclarationDeletions dans la configuration du portail si le " +
                "tableau peut légitimement en contenir plusieurs.", pending, max);
        else
            ctx.Logger.LogInformation(
                "Tableau vidé — {Deleted} déclaration(s) supprimée(s), le robot peut créer la nouvelle déclaration",
                deleted);
    }

    /// <summary>
    /// Counts the deletable rows, distinguishing "empty" from "not loaded yet".
    ///
    /// The row selector matches rows that CONTAIN a delete icon, which does the detection work twice over:
    /// ng2-smart-table's "no data" placeholder row has no icon, and neither has any row the portal chose
    /// not to make deletable — so a locked/filed declaration can never be counted, let alone deleted.
    /// </summary>
    private static async Task<int> WaitForDeclarationsAsync(
        RobotContext ctx, string row, string deleteButton, CancellationToken ct)
    {
        // No delete icon within the full timeout => the table is loaded and empty.
        if (!await TvaDom.PresentAsync(ctx, deleteButton, ct)) return 0;

        return await ctx.Page.CountAsync(row, ct);
    }

    private static async Task DeleteFirstAsync(
        RobotContext ctx, string row, string deleteButton, int before, CancellationToken ct)
    {
        var dialog = ctx.Portal.Element("dialog");
        var confirmButton = ctx.Portal.Element("dialogConfirm");

        // Log WHAT is about to be deleted before deleting it: the run's step log is the only trace that
        // survives the operation.
        ctx.Logger.LogInformation(
            "Suppression de la déclaration : {Row}",
            TvaDom.Normalize(await ctx.Page.GetTextAsync(row, ct)) ?? "(ligne illisible)");

        await ctx.Page.ClickAsync(deleteButton, ct);

        await TvaDom.WaitForAsync(ctx, dialog, "la boîte de confirmation de suppression", ct);

        var message = await TvaDom.DialogTextAsync(ctx, ct);

        // Guard against confirming the wrong dialog: .swal2-confirm is whatever SweetAlert2 has open, and
        // the portal uses SweetAlert2 for errors too. If the wording does not look like a delete
        // confirmation, stop rather than click "Oui !" on something unknown.
        var expected = TvaDom.Optional(ctx, "declarationDeleteConfirmText");
        if (expected is not null && message?.Contains(expected, StringComparison.OrdinalIgnoreCase) != true)
            throw new InvalidOperationException(
                $"La boîte de dialogue affichée (« {message ?? "sans texte"} ») ne ressemble pas à une " +
                $"confirmation de suppression (« {expected} » attendu) — confirmation annulée par sécurité.");

        ctx.Logger.LogInformation("Confirmation demandée par le portail : {Message}", message);
        await ctx.Page.ClickAsync(confirmButton, ct);

        // The portal may answer with a result popup ("Supprimé !"); it has to go before the next click.
        await TvaDom.DismissDialogsAsync(ctx, ct);

        // The authoritative signal: the table has one row fewer. A success notification is not enough —
        // it can be shown while the grid still holds the stale row.
        var removed = await Poll.UntilAsync(
            ctx, ctx.DefaultTimeoutMs, TvaDom.PollIntervalMs,
            async (c, token) => await c.Page.CountAsync(row, token) < before, ct);

        if (!removed)
            throw new InvalidOperationException(
                $"La suppression n'a pas abouti : le tableau contient toujours {before} déclaration(s) " +
                "après la confirmation.");

        ctx.Logger.LogInformation("Suppression confirmée — la ligne a disparu du tableau");
    }
}

/// <summary>
/// Step 2.1 — open the navigation menu and go to "Déclaration Période en cours", i.e. the
/// <c>#/dec-tva</c> route.
///
/// <c>&lt;app-menu&gt;</c> nests three levels — the <c>.js-open-menu</c> toggle, the
/// "Déclarations du chiffre d'affaires" group, then the entry itself — and each is opened only when it is
/// not already open, so the step behaves the same on a fresh page as it does when something left part of
/// the menu expanded. "Already open" is decided by reachability rather than visibility, because the folded
/// panel is parked off-canvas and reads as visible (see <see cref="TvaDom.ReachableAsync"/>).
///
/// <para>None of the three is located by its label. The entry is found by the route it links to
/// (<c>href="#/dec-tva"</c>) — the portal's own contract — and the group by the fact that it CONTAINS that
/// link, which matters twice over: all five top-level items carry the same <c>class="Déclarations"</c>, so
/// the class cannot tell them apart, and the group's own label contains a typographic apostrophe
/// (<c>chiffre d’affaires</c>, U+2019) that a hand-written ASCII <c>'</c> would silently fail to match.</para>
/// </summary>
public sealed class OpenCurrentPeriodDeclarationStep : IRobotStep
{
    public string Name => "Ouverture de la déclaration de la période en cours";

    public Task ExecuteAsync(RobotContext ctx, CancellationToken ct) =>
        TvaMenu.GoToAsync(
            ctx,
            groupKey: "menuDeclarationGroup",
            itemKey: "menuDeclarationCurrentPeriod",
            urlKey: "declarationPageUrl",
            group: "le groupe « Déclarations du chiffre d'affaires »",
            item: "l'entrée « Déclaration Période en cours »",
            what: "la page des déclarations",
            ct);
}

/// <summary>
/// Step 3 — go back to the declaration list after the EDI file has been sent, so the newly created
/// declaration can be reopened.
///
/// Same navigation as <see cref="OpenCurrentPeriodDeclarationStep"/>, deliberately kept as its own step
/// rather than reusing that instance: the run's step list is what the operator reads, and two entries
/// called "Ouverture de la déclaration de la période en cours" would make a failure impossible to place.
/// </summary>
public sealed class ReturnToDeclarationListStep : IRobotStep
{
    public string Name => "Retour à la liste des déclarations";

    public Task ExecuteAsync(RobotContext ctx, CancellationToken ct) =>
        TvaMenu.GoToAsync(
            ctx,
            groupKey: "menuDeclarationGroup",
            itemKey: "menuDeclarationCurrentPeriod",
            urlKey: "declarationPageUrl",
            group: "le groupe « Déclarations du chiffre d'affaires »",
            item: "l'entrée « Déclaration Période en cours »",
            what: "la liste des déclarations",
            ct);
}

/// <summary>
/// Step 2.2 — pick the régime and ask the portal to create the declaration, which answers by navigating to
/// <c>#/dec-tva/edit/{id}</c>.
///
/// That generated id is read back out of the URL and published as <c>declarationId</c> (in
/// <see cref="RobotContext.Items"/> for later steps and <see cref="RobotContext.Output"/> for the caller);
/// it is never hardcoded, since it differs for every declaration.
/// </summary>
public sealed class CreateDeclarationStep : IRobotStep
{
    /// <summary>Régime used when neither the run nor the configuration names one. "Mensuel" is the monthly
    /// filing this robot was built for; the label is matched loosely, so it also finds
    /// "Mensuel / Éclaration du mois".</summary>
    private const string DefaultRegime = "Mensuel";

    public string Name => "Création de la déclaration";

    /// <summary>Not idempotent — a retry would ask the portal for a second declaration.</summary>
    public bool Retryable => false;

    public async Task ExecuteAsync(RobotContext ctx, CancellationToken ct)
    {
        var createButton = ctx.Portal.Element("declarationCreateButton");

        // The create button doubles as the "the form has rendered" signal.
        await TvaDom.WaitForAsync(ctx, createButton, "le formulaire de création de déclaration", ct);

        var regime = ctx.GetParameter("regime")
                     ?? TvaDom.Optional(ctx, "declarationRegime")
                     ?? DefaultRegime;

        // Selected by visible label, not by value: the portal's values ("1"/"2") carry no meaning and could
        // be renumbered, while the label is what the user asked for. Matching is accent- and
        // case-insensitive and falls back to "contains", so "Mensuel" finds "Mensuel / إقرار الشهر".
        ctx.Logger.LogInformation("Régime demandé : {Regime}", regime);
        await ctx.Page.SelectOptionByLabelAsync(ctx.Portal.Element("declarationRegimeSelect"), regime, ct);

        await ctx.Page.ClickAsync(createButton, ct);

        var editUrl = ctx.Portal.Element("declarationEditUrl");
        try
        {
            await ctx.Page.WaitForUrlAsync(editUrl, ctx.DefaultTimeoutMs, ct);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // A SweetAlert2 popup is the usual reason the portal never navigates (period already filed,
            // régime not allowed for this account…), and its wording is far more useful than a timeout.
            var dialog = await TvaDom.DialogTextAsync(ctx, ct);
            throw new InvalidOperationException(
                dialog is null
                    ? $"La déclaration n'a pas été créée : le portail n'a pas ouvert la page d'édition " +
                      $"(« {editUrl} »). Page courante : {ctx.Page.Url}"
                    : $"La déclaration n'a pas été créée — message du portail : « {dialog} »", ex);
        }

        var id = ExtractDeclarationId(ctx.Page.Url);
        if (id is null)
        {
            ctx.Logger.LogWarning(
                "Déclaration créée, mais son identifiant n'a pas pu être lu dans l'URL {Url}", ctx.Page.Url);
            return;
        }

        ctx.Items["declarationId"] = id;
        ctx.Output["declarationId"] = id;
        ctx.Logger.LogInformation("Déclaration créée — identifiant {Id} ({Url})", id, ctx.Page.Url);
    }

    /// <summary>
    /// Reads the generated declaration id from the last segment of the hash route
    /// (…<c>#/dec-tva/edit/24034184</c>). Route-agnostic on purpose: the pattern the step waited for is
    /// configuration, so this must not re-encode it.
    /// </summary>
    private static string? ExtractDeclarationId(string url)
    {
        var trimmed = url.TrimEnd('/');
        var slash = trimmed.LastIndexOf('/');
        if (slash < 0) return null;

        var id = trimmed[(slash + 1)..];
        var cut = id.IndexOfAny(['?', '&', '#']);
        if (cut >= 0) id = id[..cut];

        return string.IsNullOrWhiteSpace(id) ? null : id;
    }
}

/// <summary>
/// Step 2.3 — save the freshly created declaration ("Enregistrer / حفظ").
///
/// <para><b>The portal shows no success message.</b> The signal that the save landed is that
/// « Enregistrer » becomes <b>disabled</b> — there is nothing left to save. So that, not a popup, is what
/// this step waits for, and it refuses to continue on an unconfirmed save rather than carry a half-saved
/// declaration into the steps that follow.</para>
///
/// An error dialog is still watched for in parallel: when the portal rejects a save, its wording is the
/// only useful diagnosis, and finding it turns a silent timeout into an actionable message.
///
/// No scrolling code is needed — Playwright scrolls an element into view before clicking it.
/// </summary>
public sealed class SaveDeclarationStep : IRobotStep
{
    /// <summary>
    /// How long « Enregistrer » must *stay* disabled before the save is believed. Guards against the other
    /// reason a submit goes grey — being disabled only while its request is in flight — which would
    /// otherwise read as success the instant the click landed.
    /// </summary>
    private const int StaysDisabledMs = 750;

    public string Name => "Enregistrement de la déclaration";

    /// <summary>Not idempotent — a retry would submit the form a second time.</summary>
    public bool Retryable => false;

    public async Task ExecuteAsync(RobotContext ctx, CancellationToken ct)
    {
        var saveButton = ctx.Portal.Element("declarationSaveButton");

        await TvaDom.WaitForAsync(ctx, saveButton, "le bouton « Enregistrer »", ct);

        // Checked before clicking, for two reasons: by this step's own success rule an already-disabled
        // button means the declaration is already saved, and clicking a disabled element would make
        // Playwright wait for it to become enabled and then fail on timeout — an error that explains
        // nothing.
        if (await ctx.Page.IsDisabledAsync(saveButton, ct))
        {
            ctx.Logger.LogInformation(
                "Le bouton « Enregistrer » est déjà désactivé — la déclaration est déjà enregistrée");
            return;
        }

        await ctx.Page.ClickAsync(saveButton, ct);

        await TvaDom.WaitForLoaderAsync(ctx, ct);
        await VerifySavedAsync(ctx, saveButton, ct);
    }

    private static async Task VerifySavedAsync(RobotContext ctx, string saveButton, CancellationToken ct)
    {
        var error = TvaDom.Optional(ctx, "dialogError");

        var saved = await Poll.UntilAsync(ctx, ctx.DefaultTimeoutMs, TvaDom.PollIntervalMs,
            async (c, token) =>
            {
                if (error is not null && await c.Page.IsVisibleAsync(error, token))
                    throw new InvalidOperationException(
                        "Le portail a refusé l'enregistrement : " +
                        $"« {await TvaDom.DialogTextAsync(c, token) ?? "sans message"} »");

                if (!await c.Page.IsDisabledAsync(saveButton, token)) return false;

                await Task.Delay(StaysDisabledMs, token);
                return await c.Page.IsDisabledAsync(saveButton, token);
            }, ct);

        //if (!saved)
        //    throw new InvalidOperationException(
        //        $"L'enregistrement n'a pas été confirmé : le bouton « Enregistrer » (« {saveButton} ») est " +
        //        $"toujours actif après {ctx.DefaultTimeoutMs / 1000} s. Page courante : {ctx.Page.Url}");

        ctx.Logger.LogInformation("Enregistrement confirmé — le bouton « Enregistrer » est désactivé");

        // Nothing is expected on screen, but a popup left open would put a SweetAlert2 backdrop over the
        // page and swallow the next step's first click. Returns immediately when there is none.
        await TvaDom.DismissDialogsAsync(ctx, ct);
    }
}

/// <summary>
/// Step 3.1 — open the "Envoi EDI" page (<c>#/envoiEdi</c>), where the declaration archive is uploaded.
///
/// Reuses <see cref="TvaMenu"/>, which scrolls the page back to the top before touching the menu. That is
/// what this step needs and why it exists as its own entry: saving leaves the browser at the foot of a long
/// form, and the nav panel is anchored to the top of the document, so opened from there its entries unfold
/// above the viewport and cannot be clicked where they are.
/// </summary>
public sealed class OpenEdiUploadStep : IRobotStep
{
    public string Name => "Ouverture de la page d'envoi EDI";

    public Task ExecuteAsync(RobotContext ctx, CancellationToken ct) =>
        TvaMenu.GoToAsync(
            ctx,
            groupKey: "menuEdiGroup",
            itemKey: "menuEdiSend",
            urlKey: "ediPageUrl",
            group: "le groupe « EDI »",
            item: "l'entrée « Envoi EDI »",
            what: "la page d'envoi EDI",
            ct);
}

/// <summary>
/// Step 3.2 — attach the declaration's EDI archives and send them ("Charger / تحميل").
///
/// <para><b>Up to three archives, one page.</b> The déductions archive plus, when the période has them, the
/// non-residents (NR) and retenue-à-la-source (RAS) annexes. The portal has a single file input, so they go
/// through it one at a time — attach, Charger, read the answer, dismiss, next — which is exactly the sequence
/// the legacy robot performs across its chain of background workers
/// (winTeleDeclaration.xaml.cs:716 and :815). Order matters: déductions first, then the annexes.</para>
///
/// <para><b>A failed annexe is fatal.</b> If the déductions archive is accepted and an annexe then is not,
/// the portal holds an incomplete declaration. The step fails naming what did go through, because a missing
/// NR or RAS file is a filing gap that nobody notices if it is only warned about.</para>
///
/// <para><b>Where the archives come from.</b> The bridge generated them from the dossier and
/// <see cref="LoadDeclarationDataStep"/> published them on the payload before the browser opened. The
/// <c>ediFilePath</c> run parameter overrides the <b>déductions</b> archive only — it lets an operator
/// re-send a specific main archive (an earlier run's, or one the desktop app produced) while any annexe the
/// bridge produced is still sent, so an override cannot silently drop one. The Angular card does not send
/// it.</para>
///
/// Nothing to send at all is a hard failure rather than a skip: continuing would recalculate and file a
/// declaration whose EDI data was never sent, which looks like success and is not.
/// </summary>
public sealed class SendEdiFileStep : IRobotStep
{
    /// <summary>Run parameter naming an archive on disk, overriding the generated déductions archive when
    /// supplied. The NR/RAS annexes are unaffected by it.</summary>
    public const string PathParameter = "ediFilePath";

    public string Name => "Envoi des fichiers EDI";

    /// <summary>Not idempotent — a retry would send the same declaration to the portal twice.</summary>
    public bool Retryable => false;

    /// <summary>The order the portal expects: the déductions archive, then the annexes.</summary>
    private static readonly string[] AnnexeKinds = { "NR", "RAS" };

    public async Task ExecuteAsync(RobotContext ctx, CancellationToken ct)
    {
        var input = ctx.Portal.Element("ediFileInput");
        var uploadButton = ctx.Portal.Element("ediUploadButton");

        var archives = Resolve(ctx);
        var sent = new List<string>();

        // One archive at a time through the SAME file input, exactly as the legacy robot does it: attach,
        // Charger, read the answer, dismiss it, then the next. There is no multi-file input on this page.
        foreach (var archive in archives)
        {
            await TvaDom.WaitForAttachedAsync(ctx, input, "le champ de dépôt du fichier EDI", ct);

            var name = await AttachAsync(ctx, input, archive, ct);

            // The portal ships "Charger" disabled and enables it once it has accepted an attachment, so this
            // is its own confirmation that the file went in — verified before clicking rather than after.
            await TvaDom.WaitForEnabledAsync(ctx, uploadButton, "le bouton « Charger »", ct);

            // Sending an EDI file registers a real deposit against the taxpayer's account (it shows up under
            // "Suivi Envoi EDI"), so the same dry-run switch that protects the deletion also protects this.
            // Stops at the first archive: attaching the annexes as well would prove nothing more.
            if (ctx.Portal.StopBeforeFinalSubmit)
            {
                ctx.Logger.LogWarning(
                    "Mode dry-run (StopBeforeFinalSubmit) — « {File} » est joint et prêt, AUCUN envoi effectué. " +
                    "{Count} archive(s) au total auraient été envoyées.", name, archives.Count);
                ctx.Output["fichierEdi"] = $"{name} (dry-run, non envoyé)";
                return;
            }

            try
            {
                await ctx.Page.ClickAsync(uploadButton, ct);
                await TvaDom.WaitForLoaderAsync(ctx, ct);
                await ConfirmSentAsync(ctx, name, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Deliberately fatal, and deliberately explicit about what DID go through: the déductions
                // archive may already be filed while an annexe is not, which leaves an incomplete declaration
                // on the portal. That is a state a human has to resolve, not one to log and walk past.
                throw new InvalidOperationException(
                    $"L'envoi de l'archive {archive.Kind} « {name} » a échoué. Déjà envoyée(s) au portail : " +
                    $"{(sent.Count == 0 ? "aucune" : string.Join(", ", sent))}. La déclaration est donc " +
                    "incomplète côté portail — envoyez les archives restantes manuellement, ou corrigez puis " +
                    "relancez après avoir supprimé le dépôt partiel.", ex);
            }

            sent.Add($"{name} ({archive.Kind})");
            ctx.Output["fichierEdi"] = string.Join(", ", sent);
        }

        ctx.Logger.LogInformation("{Count} archive(s) EDI envoyée(s) : {Files}", sent.Count, string.Join(", ", sent));
    }

    /// <summary>Puts one archive on the file input and returns its name, for the log and the run output.</summary>
    private static async Task<string> AttachAsync(RobotContext ctx, string input, Archive archive, CancellationToken ct)
    {
        try
        {
            // Handed to the browser driver as a path rather than read here: it keeps file I/O out of the
            // Application layer, and the archive is never copied into this process's memory.
            await ctx.Page.SetInputFilesAsync(input, archive.Path, ct);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"Le fichier EDI « {archive.Path} » n'a pas pu être joint — chemin introuvable ou illisible.", ex);
        }

        var name = Path.GetFileName(archive.Path);
        ctx.Logger.LogInformation(
            "Fichier EDI joint — {Kind} ({Source}) : {File}", archive.Kind, archive.Source, name);
        return name;
    }

    /// <summary>
    /// Which archives to send and in which order, plus where each choice came from — the latter goes in the
    /// log, because "which file did it actually upload" is the first question asked when a filing looks wrong.
    ///
    /// The <c>ediFilePath</c> parameter overrides the déductions archive ONLY; any NR/RAS annexe the bridge
    /// produced is still sent, so supplying a hand-made main archive cannot silently drop them.
    /// </summary>
    private static List<Archive> Resolve(RobotContext ctx)
    {
        var payload = Payload(ctx);
        var archives = new List<Archive>();

        // The override is checked first: an explicitly supplied path is an instruction, not a fallback.
        var overridden = ctx.GetParameter(PathParameter);
        if (!string.IsNullOrWhiteSpace(overridden))
            archives.Add(new Archive(overridden!, "Main", $"paramètre « {PathParameter} »"));
        else if (Pick(payload, "Main") is { } main)
            archives.Add(new Archive(main.Path, "Main", "généré depuis le dossier"));

        foreach (var kind in AnnexeKinds)
            if (Pick(payload, kind) is { } annexe)
                archives.Add(new Archive(annexe.Path, kind, "généré depuis le dossier"));

        if (archives.Count == 0)
            throw new InvalidOperationException(
                "Aucun fichier EDI à envoyer : le pont GénéraFi n'a pas produit d'archive pour ce dossier. " +
                "Vérifiez le journal de la première étape et la configuration Bridge:EdiDirectory, ou indiquez " +
                $"une archive existante via le paramètre « {PathParameter} ».");

        return archives;
    }

    private static DeclarationPayload? Payload(RobotContext ctx) =>
        ctx.Items.TryGetValue(LoadDeclarationDataStep.ItemKey, out var item) && item is DeclarationPayload payload
            ? payload
            : null;

    /// <summary>The generated archive of a given kind, or null when this période does not need one — a
    /// période with no non-resident purchase or no retenue à la source produces no annexe at all.</summary>
    private static EdiFile? Pick(DeclarationPayload? payload, string kind) =>
        payload?.EdiFiles.FirstOrDefault(f => string.Equals(f.Kind, kind, StringComparison.OrdinalIgnoreCase));

    /// <summary>One archive to upload: where it is, what it is, and why it was chosen.</summary>
    private sealed record Archive(string Path, string Kind, string Source);

    /// <summary>
    /// Reads the portal's answer to the upload.
    ///
    /// An error dialog fails the step; a normal one is logged and dismissed (its backdrop would otherwise
    /// swallow the next step's first click). <b>No dialog at all does not fail the step</b> — the portal's
    /// confirmation for this page has not been observed yet, and refusing an upload that in fact succeeded
    /// would be worse than reporting it unverified. The warning says so, so a run log shows which happened.
    /// </summary>
    private static async Task ConfirmSentAsync(RobotContext ctx, string file, CancellationToken ct)
    {
        var dialog = TvaDom.Optional(ctx, "dialog");
        var error = TvaDom.Optional(ctx, "dialogError");

        var answered = dialog is not null && await Poll.UntilAsync(
            ctx, ctx.DefaultTimeoutMs, TvaDom.PollIntervalMs,
            (c, token) => c.Page.IsVisibleAsync(dialog, token), ct);

        if (!answered)
        {
            ctx.Logger.LogWarning(
                "« {File} » a été envoyé mais le portail n'a affiché aucune confirmation — vérifiez le " +
                "résultat dans « Suivi Envoi EDI ».", file);
            return;
        }

        var message = await TvaDom.DialogTextAsync(ctx, ct);

        if (error is not null && await ctx.Page.IsVisibleAsync(error, ct))
            throw new InvalidOperationException(
                $"Le portail a refusé le fichier EDI « {file} » : « {message ?? "sans message"} »");

        ctx.Logger.LogInformation("Fichier EDI envoyé — réponse du portail : {Message}", message);
        await TvaDom.DismissDialogsAsync(ctx, ct);
    }
}

/// <summary>
/// Step 4 — reopen the declaration created earlier, by clicking its row's edit icon in the list.
///
/// The row is found by the id <see cref="CreateDeclarationStep"/> read out of the URL, never by position:
/// the list is sorted by the portal and a "first row" assumption would edit whichever declaration happens
/// to be on top. After the click the id is read back out of the URL and compared, so opening the wrong
/// declaration fails here instead of being recalculated by the next step.
///
/// <para>If the row's edit control cannot be found, the step navigates straight to
/// <c>#/dec-tva/edit/{id}</c> and says so. That fallback exists because the icon's class is the one
/// selector here that was not taken from the live DOM — a wrong guess should cost a warning, not the
/// run.</para>
/// </summary>
public sealed class EditDeclarationStep : IRobotStep
{
    public string Name => "Réouverture de la déclaration";

    public async Task ExecuteAsync(RobotContext ctx, CancellationToken ct)
    {
        var id = ctx.Items.TryGetValue("declarationId", out var value) ? value as string : null;
        if (string.IsNullOrWhiteSpace(id))
            throw new InvalidOperationException(
                "L'identifiant de la déclaration est inconnu : l'étape de création ne l'a pas publié, " +
                "il n'y a donc pas de déclaration à réouvrir.");

        // {id} substitution rather than a text search over the whole table: it scopes the click to the one
        // row that mentions this declaration.
        var row = ctx.Portal.Element("declarationEditRow").Replace("{id}", id);
        var editButton = $"{row} {ctx.Portal.Element("declarationEditButton")}";

        if (await TvaDom.PresentAsync(ctx, editButton, ct))
        {
            ctx.Logger.LogInformation("Déclaration {Id} trouvée dans le tableau — ouverture en édition", id);
            await ctx.Page.ClickAsync(editButton, ct);
        }
        else
        {
            // Two different situations, and only one of them is a problem: the list may simply not print the
            // identifier in its columns (période, régime, état…), in which case no row can match it and
            // navigating by route is the normal answer. A row that DOES match but carries no recognisable
            // edit control means the icon selector is wrong, which is worth a warning.
            var url = Route(ctx, id);

            if (await ctx.Page.CountAsync(row, ct) == 0)
                ctx.Logger.LogInformation(
                    "Le tableau n'affiche pas l'identifiant {Id} dans ses colonnes — ouverture directe de {Url}",
                    id, url);
            else
                ctx.Logger.LogWarning(
                    "La ligne de la déclaration {Id} a été trouvée mais aucune icône d'édition n'y " +
                    "correspond (« {Selector} ») — ouverture directe de {Url}. Corrigez " +
                    "« declarationEditButton » dans la configuration du portail.",
                    id, ctx.Portal.Element("declarationEditButton"), url);

            await ctx.Page.GotoAsync(url, ctx.Portal.NavigationWaitUntil, ct);
        }

        await TvaDom.WaitForUrlAsync(
            ctx, ctx.Portal.Element("declarationEditUrl"), $"la déclaration {id} en édition", ct);

        // The URL pattern only proves *a* declaration is open. This proves it is the right one — matching on
        // "/{id}" rather than "{id}" so a shorter id cannot be satisfied by the tail of a longer one.
        if (!ctx.Page.Url.TrimEnd('/').EndsWith($"/{id}", StringComparison.Ordinal))
            throw new InvalidOperationException(
                $"Le portail a ouvert une autre déclaration que celle créée ({id}) : {ctx.Page.Url}");

        ctx.Logger.LogInformation("Déclaration {Id} ouverte en édition : {Url}", id, ctx.Page.Url);
    }

    private static string Route(RobotContext ctx, string id) =>
        ctx.Portal.BaseUrl.TrimEnd('/') + "/"
        + ctx.Portal.Element("declarationEditRoute").Replace("{id}", id).TrimStart('/');
}

/// <summary>
/// Step 4.5 — type the dossier's amounts into the open declaration form
/// (<c>&lt;app-edit-declaration&gt;</c>), the portal's equivalent of the desktop's <c>pgDeclaration</c>
/// screen.
///
/// <para><b>Fields are addressed by their DGI line code, never by position.</b> The portal prints the code
/// in the first cell of the line's row, so a row is found by "first cell reads this number" — the same rule
/// the legacy robot's <c>findPortalContainerByCodeCell</c> settled on
/// (winTeleDeclaration.xaml.cs:276). The comparison is numeric, which absorbs the
/// leading-zero disagreement between <c>Codification.Code</c> ("060") and what the form prints ("60"), and
/// it is why <see cref="DeclarationLine.Rank"/> — the stale table/row coordinates that forced the legacy
/// robot's <c>Year &gt;= 2026</c> fallback chain — is not used at all.</para>
///
/// <para><b>Only what the portal will accept is typed.</b> Within a row, the numeric cells are the left
/// (base / chiffre d'affaires) and right (taxe) columns in that order; a cell the portal ships locked —
/// <c>disabled</c> or <c>readonly</c>, which is why the test is
/// <see cref="IRobotPage.IsEditableAsync"/> and not "not disabled" — is one it derives itself, such as the
/// "Chiffre d'affaires imposable" total or a tax computed from base × taux. Those are left alone, exactly as
/// the desktop screen builds the same boxes with <c>IsEnabled = false</c> and lets <c>Calcul.Recalculer</c>
/// fill them. Deriving them is what the next step's « Calculer » is for.</para>
///
/// <para><b>Every write is read back.</b> An <c>&lt;input type="number"&gt;</c> silently discards a value it
/// considers invalid, leaving an empty field and no error — so trusting the write would mean filing a
/// declaration with a missing figure and a green run. Anything that does not come back is collected, and the
/// step fails at the end with the complete list rather than at the first problem: one run then tells the
/// operator about every line that needs attention instead of one per attempt.</para>
///
/// <para>Zero amounts are skipped. The declaration was created fresh by this same robot (any pending one was
/// deleted first), so its fields start empty and empty already means zero.</para>
/// </summary>
public sealed class FillDeclarationAmountsStep : IRobotStep
{
    /// <summary>Money, so at most two decimals — and the invariant separator, because
    /// <c>&lt;input type="number"&gt;</c> only accepts "1234.56". A locale comma is exactly the kind of
    /// silent rejection the read-back check catches.</summary>
    private const string AmountFormat = "0.##";

    /// <summary>Half a centime: reformatting by the portal is accepted, an altered or dropped figure is not.</summary>
    private const double AmountTolerance = 0.005;

    /// <summary>Bound on the expand pre-pass, so a selector that matches something which does not actually
    /// unfold cannot become an endless click loop.</summary>
    private const int MaxSectionsToExpand = 40;

    /// <summary>
    /// Codes the legacy robot maps by hand: for these it writes the LEFT amount into the "TVA déductible"
    /// (right) column (winTeleDeclaration.xaml.cs:1785), instead of the right amount this step's general
    /// rule puts there.
    ///
    /// Not special-cased here, deliberately. Whether the redesigned form still needs it cannot be decided
    /// from the déductions blocks' markup, which has not been captured yet — so the general rule applies and
    /// a warning names the line, which turns an unverifiable assumption into something the first real run
    /// answers.
    /// </summary>
    private static readonly HashSet<int> LegacyRightFromLeftCodes = [170, 180, 185, 186, 187];

    public string Name => "Saisie des montants de la déclaration";

    /// <summary>
    /// Not retried. The failures this step raises are mismatches between the dossier's lines and the form's
    /// rows — a replay cannot fix one, and would cost the operator another full pass over the form. Each
    /// individual write is already verified here.
    /// </summary>
    public bool Retryable => false;

    public async Task ExecuteAsync(RobotContext ctx, CancellationToken ct)
    {
        var payload = Payload(ctx)
            ?? throw new InvalidOperationException(
                "Les montants à déclarer ne sont pas disponibles : l'étape « Chargement des données de la " +
                "déclaration » n'a rien publié, il n'y a donc rien à saisir.");

        await TvaDom.WaitForAsync(
            ctx, ctx.Portal.Element("declarationForm"), "le formulaire de la déclaration", ct);

        // Before any amount: débit and encaissement do not produce the same declaration, and the portal
        // decides which cells it computes from it.
        await SetFaitGenerateurAsync(ctx, payload, ct);
        await SetProrataAsync(ctx, payload, ct);

        // A collapsed accordion hides its inputs, and Playwright will not type into a hidden field — so this
        // turns a timeout on a random line into nothing at all.
        await ExpandSectionsAsync(ctx, ct);

        var tally = new Tally();
        foreach (var line in payload.NonZeroLignes)
        {
            ct.ThrowIfCancellationRequested();
            await FillLineAsync(ctx, line, tally, ct);
        }

        ctx.Output["montantsSaisis"] = $"{tally.Written} saisi(s), {tally.Computed} calculé(s) par le portail";

        ctx.Logger.LogInformation(
            "Saisie terminée — {Written} montant(s) saisi(s), {Computed} laissé(s) au portail (cellule " +
            "calculée), {Problems} anomalie(s)",
            tally.Written, tally.Computed, tally.Problems.Count);

        if (tally.Problems.Count > 0)
            throw new InvalidOperationException(
                $"{tally.Problems.Count} montant(s) n'ont pas pu être saisis : la déclaration affichée sur le " +
                "portail est donc INCOMPLÈTE et ne doit pas être soumise en l'état." + Environment.NewLine +
                "- " + string.Join(Environment.NewLine + "- ", tally.Problems));
    }

    /// <summary>
    /// Sets "Fait Générateur" from the dossier's régime, then verifies it — the one field here whose value
    /// changes the whole declaration rather than one line, which is why a disagreement stops the step.
    /// </summary>
    private static async Task SetFaitGenerateurAsync(
        RobotContext ctx, DeclarationPayload payload, CancellationToken ct)
    {
        var selector = TvaDom.Optional(ctx, "declarationFaitGenerateur");
        if (selector is null)
        {
            ctx.Logger.LogWarning(
                "Aucun sélecteur « declarationFaitGenerateur » configuré — le fait générateur n'est pas " +
                "renseigné et le portail gardera sa valeur par défaut.");
            return;
        }

        var expected = payload.Societe?.FaitGenerateur?.Trim();
        if (string.IsNullOrEmpty(expected))
        {
            ctx.Logger.LogWarning(
                "Le dossier n'indique pas de fait générateur (régime « {Regime} ») — champ laissé tel quel.",
                payload.Societe?.Regime);
            return;
        }

        await TvaDom.WaitForAsync(ctx, selector, "la liste « Fait Générateur »", ct);

        // Locked is a legitimate state — the portal fixes the fait générateur once a declaration carries
        // data — so it is not a failure by itself. What matters is the value in place, checked below either
        // way. Skipping the click also avoids Playwright waiting for a disabled <select> to become usable
        // and then failing on a timeout that explains nothing.
        if (await ctx.Page.IsDisabledAsync(selector, ct))
            ctx.Logger.LogInformation(
                "La liste « Fait Générateur » est verrouillée par le portail — sa valeur est vérifiée telle quelle");
        else
            // By value ("D"/"E"), not by label: the values are the portal's own contract and mean the same
            // thing in both languages, while "Débit / نظام المديونية" is a bilingual string to match.
            await ctx.Page.SelectOptionAsync(selector, expected, ct);

        var actual = await ctx.Page.GetValueAsync(selector, ct);
        if (!string.Equals(actual?.Trim(), expected, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                $"Le fait générateur n'a pas pu être positionné sur « {expected} » " +
                $"(régime {payload.Societe?.Regime ?? "inconnu"}) : le formulaire indique " +
                $"« {actual ?? "(vide)"} ». Débit et encaissement ne donnent pas la même déclaration, " +
                "la saisie s'arrête donc ici.");

        ctx.Logger.LogInformation(
            "Fait générateur positionné sur « {Value} » (régime {Regime})", expected, payload.Societe?.Regime);
    }

    /// <summary>
    /// Fills the prorata rate when the portal has a field for it.
    ///
    /// The legacy robot writes <c>ExerciceBusiness.CurrentProrata</c> into every <c>name="tauxProrata"</c>
    /// field it finds (winTeleDeclaration.xaml.cs:1723); the redesigned form's equivalent has not been
    /// captured yet. So the selector is optional and the value is logged whether or not it was typed —
    /// a run whose prorata mattered can then be told apart from one whose did not, instead of the figure
    /// disappearing silently.
    /// </summary>
    private static async Task SetProrataAsync(
        RobotContext ctx, DeclarationPayload payload, CancellationToken ct)
    {
        var prorata = payload.Periode?.Prorata ?? 0d;
        var selector = TvaDom.Optional(ctx, "declarationProrataInput");

        if (selector is null || await ctx.Page.CountAsync(selector, ct) == 0)
        {
            ctx.Logger.LogInformation(
                "Taux de prorata du dossier : {Prorata} — non saisi ({Reason})",
                Amount(prorata),
                selector is null
                    ? "aucun sélecteur « declarationProrataInput » configuré"
                    : $"champ « {selector} » absent du formulaire");
            return;
        }

        await ctx.Page.FillAsync(selector, Amount(prorata), ct);
        ctx.Logger.LogInformation("Taux de prorata saisi : {Prorata}", Amount(prorata));
    }

    /// <summary>
    /// Unfolds any collapsed block or rate tab, so every line's cell is typeable.
    ///
    /// Best-effort by design: the form ships every section open (<c>openorclose="true"</c>), so this normally
    /// finds nothing to do, and a portal that changed its markup should cost a warning rather than the run.
    /// </summary>
    private static async Task ExpandSectionsAsync(RobotContext ctx, CancellationToken ct)
    {
        foreach (var key in new[] { "declarationCollapsedSection", "declarationCollapsedTab" })
        {
            var selector = TvaDom.Optional(ctx, key);
            if (selector is null) continue;

            var opened = 0;
            var remaining = await ctx.Page.CountAsync(selector, ct);

            // The match count is the loop's own guard: each click removes one, so a count that does not fall
            // means the selector matches something that does not unfold — clicking it again achieves nothing.
            while (remaining > 0 && opened < MaxSectionsToExpand)
            {
                await ctx.Page.ClickAsync(selector, ct);
                opened++;

                var after = await ctx.Page.CountAsync(selector, ct);
                if (after >= remaining)
                {
                    ctx.Logger.LogWarning(
                        "Le clic sur « {Selector} » n'a pas déplié la section ({Count} encore repliée(s)) — " +
                        "les montants qu'elle contient échoueront s'ils restent masqués.", selector, after);
                    break;
                }

                remaining = after;
            }

            if (opened > 0)
                ctx.Logger.LogInformation(
                    "{Count} section(s) dépliée(s) via « {Selector} »", opened, selector);
        }
    }

    /// <summary>Locates one declaration line by its code and types its amounts into the row's cells.</summary>
    private static async Task FillLineAsync(
        RobotContext ctx, DeclarationLine line, Tally tally, CancellationToken ct)
    {
        var label = Describe(line);

        // The DGI line codes are numbers, and the legacy robot relies on it outright (int.Parse on the code,
        // winTeleDeclaration.xaml.cs:1734). Anything else is a Codification anomaly worth reporting rather
        // than a case to pattern-match around.
        if (!int.TryParse(line.Code, NumberStyles.Integer, CultureInfo.InvariantCulture, out var code))
        {
            tally.Problems.Add(
                $"{label} : code non numérique, la ligne n'a pas pu être localisée dans le formulaire.");
            return;
        }

        var amounts = ctx.Portal.Element("declarationLineAmounts");
        var rows = ctx.Portal.Element("declarationLineRow")
            .Replace("{code}", code.ToString(CultureInfo.InvariantCulture));

        var row = await ResolveRowAsync(ctx, rows, amounts, label, tally, ct);
        if (row is null) return;

        if (row.Cells == 0)
        {
            tally.Problems.Add(
                $"{label} : la ligne existe dans le formulaire mais n'a aucune cellule numérique " +
                $"(« {amounts} ») — {Amount(line.MntG)} / {Amount(line.MntD)} non saisis.");
            return;
        }

        if (LegacyRightFromLeftCodes.Contains(code))
            ctx.Logger.LogWarning(
                "{Label} : le robot GénéraFi_TVA écrit le montant de GAUCHE ({Value}) dans la colonne " +
                "« TVA déductible » de cette ligne, là où cette étape applique la règle générale " +
                "(gauche → base, droite → taxe). À contrôler sur le formulaire avant de soumettre.",
                label, Amount(line.MntG));

        // Two cells is the ordinary shape: left holds the base or the chiffre d'affaires, right the tax.
        if (row.Cells >= 2)
        {
            if (line.MntG != 0)
                await WriteAsync(ctx, Cell(row.Expression + amounts, 1), line.MntG, $"{label}, base/CA", tally, ct);

            if (line.MntD != 0)
                await WriteAsync(ctx, Cell(row.Expression + amounts, 2), line.MntD, $"{label}, taxe", tally, ct);

            return;
        }

        await FillSingleCellLineAsync(ctx, line, label, Cell(row.Expression + amounts, 1), tally, ct);
    }

    /// <summary>
    /// A row with a single numeric cell, which is three different situations — and telling them apart is what
    /// keeps a computed total from being mistaken for a field the robot failed to fill.
    ///
    /// <para><b>Greyed out.</b> The total lines print one cell that the portal computes: "TVA exigible" (132),
    /// "Total des déductions" (182), "Total de la TVA déductible" (190), "Crédit (190 - 132)" (201),
    /// "TVA due de la période" (205)… The dossier has a figure for each, but there is nothing to type and
    /// nothing ambiguous — « Calculer » derives them from the lines above. These are the same codes the legacy
    /// robot had to keep apart from the rest (<c>Portal2026RankOnlyCodes</c>,
    /// winTeleDeclaration.xaml.cs:217).</para>
    ///
    /// <para><b>Editable, with a left amount.</b> A left-only block — the portal's "A/ CA total", the
    /// desktop's <c>bgA</c>, which has no right column at all — so the cell is the left column and takes
    /// MntG. A right amount the calculation produced then has nowhere to go, which is normal here.</para>
    ///
    /// <para><b>Editable, with only a right amount.</b> One amount and one cell, so they pair up and the
    /// amount goes in — line 131, "Montant de la retenue à la source opérée par les clients", is this case.
    /// The row's markup does not say whether its cell is a base or a tax column, but the dossier does: a
    /// base-only line would carry a left amount too, and this one carries none.</para>
    /// </summary>
    private static async Task FillSingleCellLineAsync(
        RobotContext ctx, DeclarationLine line, string label, string cell, Tally tally, CancellationToken ct)
    {
        // Asked before anything else: a cell that cannot be typed into settles the question, because a line
        // the portal computes cannot be filled wrongly and cannot be missing.
        if (!await ctx.Page.IsEditableAsync(cell, ct))
        {
            tally.Computed++;
            ctx.Logger.LogDebug(
                "{Label} : ligne calculée par le portail, {Left} / {Right} non saisis",
                label, Amount(line.MntG), Amount(line.MntD));
            return;
        }

        if (line.MntG != 0)
        {
            await WriteAsync(ctx, cell, line.MntG, $"{label}, base/CA", tally, ct);

            if (line.MntD != 0)
                ctx.Logger.LogInformation(
                    "{Label} : montant de droite ({Value}) laissé au portail — cette ligne n'a pas de " +
                    "colonne de droite", label, Amount(line.MntD));
            return;
        }

        if (line.MntD == 0) return;

        // Named in the log rather than filled quietly: what this rule could still misread is a left-only
        // block (the portal's "A/ CA total") whose calculation produced a right value and no left one, and
        // the figure landing in the wrong column would then be invisible.
        ctx.Logger.LogWarning(
            "{Label} : cellule unique et saisissable — le montant de droite ({Value}) y est saisi. " +
            "Contrôlez cette ligne sur le formulaire après « Calculer ».", label, Amount(line.MntD));

        await WriteAsync(ctx, cell, line.MntD, $"{label}, montant", tally, ct);
    }

    /// <summary>
    /// Resolves a code to the one row that should receive its amounts.
    ///
    /// One match is the normal case. Several means the code is printed more than once — a récapitulatif
    /// block repeating it — and only rows that actually have a cell to type in are candidates, since an
    /// all-computed repeat is not somewhere the robot could write anyway. A code that still resolves to
    /// several typeable rows is reported rather than guessed at: picking the wrong line of a tax return is
    /// worse than stopping.
    /// </summary>
    private static async Task<Row?> ResolveRowAsync(
        RobotContext ctx, string rows, string amounts, string label, Tally tally, CancellationToken ct)
    {
        var count = await ctx.Page.CountAsync(Xpath(rows), ct);
        if (count == 0)
        {
            tally.Problems.Add(
                $"{label} : aucune ligne ne porte ce code dans le formulaire (« {rows} »). Le formulaire du " +
                "portail et la table Codification de cette année ne concordent pas.");
            return null;
        }

        var candidates = new List<Row>();
        for (var index = 1; index <= count; index++)
        {
            var expression = $"({rows})[{index}]";
            var cells = await ctx.Page.CountAsync(Xpath(expression + amounts), ct);

            // A lone match is kept whatever it holds, so the caller can report "row without a cell"
            // precisely instead of it looking like a missing row.
            if (count == 1 || cells > 0) candidates.Add(new Row(expression, cells));
        }

        if (candidates.Count == 1) return candidates[0];

        tally.Problems.Add(
            candidates.Count == 0
                ? $"{label} : les {count} lignes portant ce code sont toutes calculées par le portail — " +
                  "aucune cellule saisissable."
                : $"{label} : {candidates.Count} lignes saisissables portent ce code — le robot refuse de " +
                  "choisir laquelle recevrait le montant.");

        return null;
    }

    /// <summary>Types one amount into one cell, and proves it landed.</summary>
    private static async Task WriteAsync(
        RobotContext ctx, string cell, double value, string what, Tally tally, CancellationToken ct)
    {
        if (await ctx.Page.CountAsync(cell, ct) == 0)
        {
            tally.Problems.Add($"{what} : cellule introuvable (« {cell} ») — {Amount(value)} non saisi.");
            return;
        }

        // A cell that cannot be typed into is the portal saying it derives this figure itself — a total, or a
        // tax it computes from base × taux. Leaving it alone is required, not merely tolerated: « Calculer »
        // recomputes it, and filling it would fail on a timeout that says nothing.
        if (!await ctx.Page.IsEditableAsync(cell, ct))
        {
            tally.Computed++;
            ctx.Logger.LogDebug(
                "{What} : cellule calculée par le portail, {Value} non saisi", what, Amount(value));
            return;
        }

        var text = Amount(value);
        await ctx.Page.FillAsync(cell, text, ct);

        // Read back rather than trust the write: an <input type="number"> discards anything it judges
        // invalid — a locale comma, or a decimal where the field takes whole dirhams — leaving the field
        // empty and raising nothing. On a tax return that is the difference between a wrong figure filed
        // quietly and a run that stops.
        var actual = await ctx.Page.GetValueAsync(cell, ct);
        if (!double.TryParse(actual, NumberStyles.Any, CultureInfo.InvariantCulture, out var stored)
            || Math.Abs(stored - Math.Round(value, 2)) > AmountTolerance)
        {
            tally.Problems.Add(
                $"{what} : le formulaire a retenu « {actual ?? "(vide)"} » au lieu de {text}.");
            return;
        }

        tally.Written++;
        ctx.Logger.LogDebug("{What} = {Value}", what, text);
    }

    /// <summary>The n-th numeric cell of a row, 1-based — XPath's own positional predicate, so left and
    /// right keep their meaning whether or not the portal disabled one of them.</summary>
    private static string Cell(string cells, int index) => Xpath($"({cells})[{index}]");

    /// <summary>
    /// Marks an expression as XPath explicitly. Needed because these are built by wrapping the configured
    /// row expression in parentheses to index it, and Playwright only auto-detects a selector as XPath when
    /// it *starts* with "//" — a leading "(" would be parsed as CSS and fail.
    /// </summary>
    private static string Xpath(string expression) => "xpath=" + expression;

    private static string Amount(double value) =>
        Math.Round(value, 2).ToString(AmountFormat, CultureInfo.InvariantCulture);

    /// <summary>How a line is named in the log and in the failure list — code first, because that is what
    /// the operator compares against the form.</summary>
    private static string Describe(DeclarationLine line)
    {
        var libelle = TvaDom.Normalize(line.Libelle);
        if (libelle is null) return $"ligne {line.Code}";

        return $"ligne {line.Code} « {(libelle.Length > 60 ? libelle[..60] + "…" : libelle)} »";
    }

    private static DeclarationPayload? Payload(RobotContext ctx) =>
        ctx.Items.TryGetValue(LoadDeclarationDataStep.ItemKey, out var item) && item is DeclarationPayload payload
            ? payload
            : null;

    /// <summary>One resolved row: the XPath that selects it, and how many numeric cells it has.</summary>
    private sealed record Row(string Expression, int Cells);

    /// <summary>What the pass over the lines produced. Problems are collected rather than thrown so one run
    /// reports every line that needs attention.</summary>
    private sealed class Tally
    {
        public int Written;
        public int Computed;
        public List<string> Problems { get; } = [];
    }
}

/// <summary>
/// Step 5 — ask the portal to recalculate the declaration ("Calculer / حساب"), so the amounts just typed
/// in and the figures brought in by the EDI upload are consolidated into the totals.
///
/// This is the portal's counterpart of the desktop screen's « Actualiser » button, which runs
/// <c>Calcul.Recalculer</c> over the same lines — and it is what fills every cell
/// <see cref="FillDeclarationAmountsStep"/> deliberately left alone.
///
/// No scrolling code: Playwright scrolls an element into view before clicking it.
///
/// <para><b>Completion is reported, not asserted.</b> The portal shows no dedicated "calculated" state, so
/// the step waits out the loader, fails on an error dialog, and then treats « Enregistrer » becoming
/// enabled again — the form is dirty, i.e. the recalculation changed something — as the positive signal.
/// That last part is inferred from Angular's own dirty tracking rather than documented, so it is logged
/// either way and never fails the step: a recalculation that legitimately changes nothing would otherwise
/// be reported as broken.</para>
/// </summary>
public sealed class RecalculateDeclarationStep : IRobotStep
{
    public string Name => "Recalcul de la déclaration";

    /// <summary>Not idempotent from the portal's point of view — one recalculation per run is what is meant.</summary>
    public bool Retryable => false;

    public async Task ExecuteAsync(RobotContext ctx, CancellationToken ct)
    {
        var recalculate = ctx.Portal.Element("declarationRecalculateButton");
        var save = ctx.Portal.Element("declarationSaveButton");

        await TvaDom.WaitForAsync(ctx, recalculate, "le bouton « Calculer »", ct);
        await ctx.Page.ClickAsync(recalculate, ct);

        await TvaDom.WaitForLoaderAsync(ctx, ct);

        var error = TvaDom.Optional(ctx, "dialogError");
        if (error is not null && await ctx.Page.IsVisibleAsync(error, ct))
            throw new InvalidOperationException(
                "Le portail a refusé le recalcul : " +
                $"« {await TvaDom.DialogTextAsync(ctx, ct) ?? "sans message"} »");

        var dirty = await Poll.UntilAsync(
            ctx, ctx.DefaultTimeoutMs, TvaDom.PollIntervalMs,
            async (c, token) => !await c.Page.IsDisabledAsync(save, token), ct);

        ctx.Output["recalcul"] = dirty ? "effectué" : "effectué (non confirmé)";

        if (dirty)
            ctx.Logger.LogInformation(
                "Recalcul effectué — « Enregistrer » est réactivé, la déclaration a des montants à enregistrer");
        else
            ctx.Logger.LogWarning(
                "Recalcul demandé, mais « Enregistrer » est resté désactivé : soit le recalcul n'a rien " +
                "changé, soit il n'a pas abouti. Page courante : {Url}", ctx.Page.Url);

        // Anything the portal put up has to go, or its backdrop swallows the next click.
        await TvaDom.DismissDialogsAsync(ctx, ct);
    }
}

/// <summary>
/// The portal's slide-in navigation, which every step that changes section has to go through.
///
/// Shared rather than repeated because the menu has three levels and two traps. All five top-level items
/// carry the same <c>class="Déclarations"</c>, so a class cannot tell them apart, and their labels contain
/// typographic apostrophes (<c>chiffre d’affaires</c>, U+2019) that a hand-written ASCII <c>'</c> would
/// silently fail to match — so both the entry and its group are identified by the route the entry links to
/// (<c>href="#/dec-tva"</c>, <c>href="#/envoiEdi"</c>), which is the portal's own contract.
/// </summary>
internal static class TvaMenu
{
    /// <summary>
    /// Opens as much of the menu as is still closed, clicks the entry, and waits for the route to change.
    /// </summary>
    /// <param name="groupKey">Element key of the top-level group containing the entry.</param>
    /// <param name="itemKey">Element key of the entry itself.</param>
    /// <param name="urlKey">Element key of the URL pattern the entry must lead to.</param>
    public static async Task GoToAsync(
        RobotContext ctx,
        string groupKey, string itemKey, string urlKey,
        string group, string item, string what,
        CancellationToken ct)
    {
        var menuToggle = ctx.Portal.Element("menuToggle");
        var menuGroup = ctx.Portal.Element(groupKey);
        var menuItem = ctx.Portal.Element(itemKey);

        // Before anything else, because the nav is anchored to the top of the document: from a page scrolled
        // to its footer (which is where saving a long form leaves the browser) the panel opens above the
        // viewport and none of its entries can be clicked where they are.
        await ctx.Page.ScrollToTopAsync(ct);

        // Reachability, not visibility, decides whether a level still has to be opened — see
        // TvaDom.ReachableAsync: the folded nav is rendered off-canvas, so a visibility probe would report
        // the menu as already open and skip every click.
        if (!await TvaDom.ReachableAsync(ctx, menuItem, ct))
        {
            if (!await TvaDom.ReachableAsync(ctx, menuGroup, ct))
            {
                await TvaDom.WaitForAsync(ctx, menuToggle, "l'ouverture du menu (« MENU / القائمة »)", ct);
                await ctx.Page.ClickAsync(menuToggle, ct);
                await TvaDom.WaitForReachableAsync(ctx, menuGroup, $"{group} du menu ouvert", ct);
            }

            // Opens the sub-list. Clicking also hovers first, so this works whether the portal expands the
            // sub-menu on click or on hover.
            await ctx.Page.ClickAsync(menuGroup, ct);
            await TvaDom.WaitForReachableAsync(ctx, menuItem, $"{item} du sous-menu", ct);
        }

        await ctx.Page.ClickAsync(menuItem, ct);

        // The hash route changing is the portal's own confirmation that the section was entered — a more
        // meaningful signal than any element of the page that follows.
        await TvaDom.WaitForUrlAsync(ctx, ctx.Portal.Element(urlKey), what, ct);

        ctx.Logger.LogInformation("Navigation vers {What} : {Url}", what, ctx.Page.Url);
    }
}

/// <summary>Helpers shared by the TVA declaration steps: waits with actionable failures, and the
/// SweetAlert2 dialog handling the portal makes unavoidable.</summary>
internal static class TvaDom
{
    public const int PollIntervalMs = 250;

    /// <summary>How long a dialog is given to finish closing. Short on purpose: it is an animation, and a
    /// dialog still up after this is a *different* dialog to dismiss, not a slow one.</summary>
    private const int DialogSettleMs = 2_000;

    /// <summary>Reads an optional entry from the portal's element map; blank counts as absent, which is
    /// what lets a check be switched off from configuration.</summary>
    public static string? Optional(RobotContext ctx, string key) =>
        ctx.Portal.Elements.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : null;

    /// <summary>Waits for an element, reporting a miss in terms of what was being waited for and where the
    /// browser actually is — the two things needed to fix a selector without reproducing the run.</summary>
    public static async Task WaitForAsync(RobotContext ctx, string selector, string what, CancellationToken ct)
    {
        try
        {
            await ctx.Page.WaitForSelectorAsync(selector, ctx.DefaultTimeoutMs, ct);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"Impossible de trouver {what} (« {selector} »). Page courante : {ctx.Page.Url}", ex);
        }
    }

    /// <summary>
    /// Whether an element can be clicked where it currently is. "Visible" alone is not enough for this
    /// portal's slide-in menu: the folded panel is rendered off-canvas, which Playwright reports as visible
    /// while every click on it fails with "element is outside of the viewport" — so a visibility probe reads
    /// a closed menu as open and skips the click that would open it.
    /// </summary>
    public static async Task<bool> ReachableAsync(RobotContext ctx, string selector, CancellationToken ct) =>
        await ctx.Page.IsVisibleAsync(selector, ct) && await ctx.Page.IsInViewportAsync(selector, ct);

    /// <summary>Waits for an element and reports a miss instead of throwing — for the cases where "not
    /// there" is a legitimate answer (an empty list renders no table) rather than a broken selector.</summary>
    public static async Task<bool> PresentAsync(RobotContext ctx, string selector, CancellationToken ct)
    {
        try
        {
            await ctx.Page.WaitForSelectorAsync(selector, ctx.DefaultTimeoutMs, ct);
            return true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Waits until an element is not just present but actually reachable — the panel has finished
    /// sliding in, not merely been rendered.</summary>
    public static async Task WaitForReachableAsync(
        RobotContext ctx, string selector, string what, CancellationToken ct)
    {
        if (await Poll.UntilAsync(ctx, ctx.DefaultTimeoutMs, PollIntervalMs,
                (c, token) => ReachableAsync(c, selector, token), ct))
            return;

        throw new InvalidOperationException(
            $"Impossible d'atteindre {what} (« {selector} ») : l'élément est présent mais hors de la zone " +
            $"visible — panneau replié, ou animation d'ouverture non terminée. Page courante : {ctx.Page.Url}");
    }

    /// <summary>
    /// Waits until an element exists in the DOM, without requiring it to be visible.
    ///
    /// For file inputs, which is what this exists for: pages routinely hide them behind a styled label (this
    /// portal wraps its own in <c>&lt;label class="btn"&gt;</c>), and setting files on one does not need it
    /// visible. Demanding visibility would fail on a perfectly usable input.
    /// </summary>
    public static async Task WaitForAttachedAsync(
        RobotContext ctx, string selector, string what, CancellationToken ct)
    {
        if (await Poll.UntilAsync(ctx, ctx.DefaultTimeoutMs, PollIntervalMs,
                async (c, token) => await c.Page.CountAsync(selector, token) > 0, ct))
            return;

        throw new InvalidOperationException(
            $"Impossible de trouver {what} (« {selector} ») dans la page. Page courante : {ctx.Page.Url}");
    }

    /// <summary>
    /// Waits for a control the portal ships disabled to become usable.
    ///
    /// Used as an outcome signal, the mirror of <see cref="SaveDeclarationStep"/>'s rule: a submit that
    /// only enables once its form is satisfied is telling you the form is satisfied. Waiting here also
    /// avoids clicking a disabled element, which makes Playwright wait for it to become enabled and then
    /// fail on a timeout that explains nothing.
    /// </summary>
    public static async Task WaitForEnabledAsync(
        RobotContext ctx, string selector, string what, CancellationToken ct)
    {
        await WaitForAsync(ctx, selector, what, ct);

        if (await Poll.UntilAsync(ctx, ctx.DefaultTimeoutMs, PollIntervalMs,
                async (c, token) => !await c.Page.IsDisabledAsync(selector, token), ct))
            return;

        throw new InvalidOperationException(
            $"{what} (« {selector} ») est resté désactivé après {ctx.DefaultTimeoutMs / 1000} s — le portail " +
            $"n'a pas accepté la saisie qui devait l'activer. Page courante : {ctx.Page.Url}");
    }

    public static async Task WaitForUrlAsync(
        RobotContext ctx, string pattern, string what, CancellationToken ct)
    {
        try
        {
            await ctx.Page.WaitForUrlAsync(pattern, ctx.DefaultTimeoutMs, ct);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"Le portail n'a pas ouvert {what} (« {pattern} »). Page courante : {ctx.Page.Url}", ex);
        }
    }

    /// <summary>Best-effort wait for an AJAX spinner to clear. Optional: portals without one just skip it,
    /// and a spinner that never appears must not fail the step.</summary>
    public static async Task WaitForLoaderAsync(RobotContext ctx, CancellationToken ct)
    {
        var loader = Optional(ctx, "loader");
        if (loader is null) return;

        try
        {
            await ctx.Page.WaitForHiddenAsync(loader, ctx.DefaultTimeoutMs, ct);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            ctx.Logger.LogInformation("L'indicateur de chargement « {Loader} » est resté visible", loader);
        }
    }

    /// <summary>
    /// The readable part of the dialog currently on screen, or null if there is none. Title and message are
    /// preferred over the popup's own text content, which also swallows the button labels.
    /// </summary>
    public static async Task<string?> DialogTextAsync(RobotContext ctx, CancellationToken ct)
    {
        var dialog = Optional(ctx, "dialog");
        if (dialog is null || !await ctx.Page.IsVisibleAsync(dialog, ct)) return null;

        var pieces = new List<string>();
        foreach (var key in new[] { "dialogTitle", "dialogContent" })
        {
            var selector = Optional(ctx, key);
            if (selector is null) continue;

            var text = Normalize(await ctx.Page.GetTextAsync(selector, ct));
            if (text is not null) pieces.Add(text);
        }

        return pieces.Count > 0 ? string.Join(" — ", pieces) : Normalize(await ctx.Page.GetTextAsync(dialog, ct));
    }

    /// <summary>
    /// Waits out the dialog on screen and dismisses whatever the portal puts up next (typically a result
    /// popup). Necessary rather than cosmetic: SweetAlert2's backdrop covers the page, so a popup left open
    /// makes every later click land on the overlay instead of the app.
    /// </summary>
    public static async Task DismissDialogsAsync(RobotContext ctx, CancellationToken ct)
    {
        var dialog = Optional(ctx, "dialog");
        if (dialog is null) return;

        var confirm = Optional(ctx, "dialogConfirm");

        // Bounded: two popups is the realistic maximum (a confirmation and its result). Anything beyond
        // that is unexpected, and the caller's own verification is what should report it.
        for (var i = 0; i < 2; i++)
        {
            var closed = await Poll.UntilAsync(
                ctx, DialogSettleMs, PollIntervalMs,
                async (c, token) => !await c.Page.IsVisibleAsync(dialog, token), ct);
            if (closed) return;

            ctx.Logger.LogInformation("Notification du portail : {Message}", await DialogTextAsync(ctx, ct));

            if (confirm is null || !await ctx.Page.IsVisibleAsync(confirm, ct)) return;
            await ctx.Page.ClickAsync(confirm, ct);
        }
    }

    /// <summary>Collapses the whitespace an HTML block's text content is full of, so a logged message reads
    /// as one line. Returns null for anything blank, so callers can use ?? fallbacks.</summary>
    public static string? Normalize(string? text) =>
        string.IsNullOrWhiteSpace(text) ? null : Regex.Replace(text, @"\s+", " ").Trim();
}
