using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using RobotAutomation.Application.Declarations;
using RobotAutomation.Application.Robots.Abstractions;

namespace RobotAutomation.Application.Robots.Steps;

/// <summary>
/// Loads the figures to declare from the client's accounting dossier, before the browser is even opened.
///
/// Running first is deliberate: a mistyped dossier path fails in seconds instead of after the operator
/// has typed a password, a CAPTCHA and an e-mailed code.
/// </summary>
public sealed class LoadDeclarationDataStep : IRobotStep
{
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

        // A période with nothing to declare usually means the wrong dossier or the wrong période, not a
        // genuinely empty month.
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
    /// Reports the EDI archive the bridge generated, so a run that later fails to upload can be told
    /// apart from one that had nothing to upload.
    /// </summary>
    private static void PublishEdi(RobotContext ctx, DeclarationPayload payload)
    {
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

/// <summary>
/// Clears any declaration already listed on the home page, because the portal will not let a new one be
/// created while one is pending.
///
/// Deleting is a real write on a real tax portal, so the row about to go is logged first, the
/// confirmation dialog's wording is checked before "Oui !" is clicked, and at most
/// <see cref="Configuration.DgiPortalOptions.MaxDeclarationDeletions"/> rows may be removed per run.
/// </summary>
public sealed class DeleteExistingDeclarationStep : IRobotStep
{
    public string Name => "Suppression de la déclaration existante";

    /// <summary>Never retried: a retry would replay a destructive action whose first attempt may already
    /// have gone through.</summary>
    public bool Retryable => false;

    public async Task ExecuteAsync(RobotContext ctx, CancellationToken ct)
    {
        var table = ctx.Portal.Element("declarationsTable");
        var row = ctx.Portal.Element("declarationRow");
        var deleteButton = ctx.Portal.Element("declarationDeleteButton");

        // Having nothing to delete is a normal outcome, not a failure — and an account with no pending
        // declaration may not render a table at all.
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

        // Creating and saving a declaration is NOT gated by the dry-run switch — that draft can be
        // deleted again, so it is reversible; this deletion is not.
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
    /// Counts the deletable rows. Only rows the portal made deletable are counted, so a locked or filed
    /// declaration can never be removed.
    /// </summary>
    private static async Task<int> WaitForDeclarationsAsync(
        RobotContext ctx, string row, string deleteButton, CancellationToken ct)
    {
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

        // Guard against confirming the wrong dialog: the portal uses the same popup for errors. If the
        // wording does not look like a delete confirmation, stop rather than click "Oui !" on something
        // unknown.
        var expected = TvaDom.Optional(ctx, "declarationDeleteConfirmText");
        if (expected is not null && message?.Contains(expected, StringComparison.OrdinalIgnoreCase) != true)
            throw new InvalidOperationException(
                $"La boîte de dialogue affichée (« {message ?? "sans texte"} ») ne ressemble pas à une " +
                $"confirmation de suppression (« {expected} » attendu) — confirmation annulée par sécurité.");

        ctx.Logger.LogInformation("Confirmation demandée par le portail : {Message}", message);
        await ctx.Page.ClickAsync(confirmButton, ct);

        await TvaDom.DismissDialogsAsync(ctx, ct);

        // The authoritative signal is the table having one row fewer: a success notification can be shown
        // while the grid still holds the stale row.
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
/// Picks the régime and asks the portal to create the declaration. The generated declaration id is read
/// back and published as <c>declarationId</c> for the later steps and for the caller.
/// </summary>
public sealed class CreateDeclarationStep : IRobotStep
{
    /// <summary>Régime used when neither the run nor the configuration names one. "Mensuel" is the monthly
    /// filing this robot was built for.</summary>
    private const string DefaultRegime = "Mensuel";

    public string Name => "Création de la déclaration";

    /// <summary>Not idempotent — a retry would ask the portal for a second declaration.</summary>
    public bool Retryable => false;

    public async Task ExecuteAsync(RobotContext ctx, CancellationToken ct)
    {
        var createButton = ctx.Portal.Element("declarationCreateButton");

        await TvaDom.WaitForAsync(ctx, createButton, "le formulaire de création de déclaration", ct);

        var regime = ctx.GetParameter("regime")
                     ?? TvaDom.Optional(ctx, "declarationRegime")
                     ?? DefaultRegime;

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
            // A popup is the usual reason the portal never navigates — période already filed, régime not
            // allowed for this account — and its wording is far more useful than a timeout.
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
/// Saves the freshly created declaration (« Enregistrer »).
///
/// The portal shows no success message: the signal that the save landed is that « Enregistrer » becomes
/// disabled — there is nothing left to save.
/// </summary>
public sealed class SaveDeclarationStep : IRobotStep
{
    private const int StaysDisabledMs = 750;

    public string Name => "Enregistrement de la déclaration";

    /// <summary>Not idempotent — a retry would submit the form a second time.</summary>
    public bool Retryable => false;

    public async Task ExecuteAsync(RobotContext ctx, CancellationToken ct)
    {
        var saveButton = ctx.Portal.Element("declarationSaveButton");

        await TvaDom.WaitForAsync(ctx, saveButton, "le bouton « Enregistrer »", ct);

        // An already-disabled button means the declaration is already saved.
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

        await TvaDom.DismissDialogsAsync(ctx, ct);
    }
}

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
/// Attaches the declaration's EDI archives and sends them (« Charger »).
///
/// <para><b>Up to three archives, one page.</b> The déductions archive plus, when the période has them,
/// the non-residents (NR) and retenue-à-la-source (RAS) annexes. The portal has a single file input, so
/// they go through it one at a time. Order matters: déductions first, then the annexes.</para>
///
/// <para><b>A failed annexe is fatal.</b> If the déductions archive is accepted and an annexe then is
/// not, the portal holds an incomplete declaration. The step fails naming what did go through, because a
/// missing NR or RAS file is a filing gap that nobody notices if it is only warned about.</para>
///
/// <para>The <c>ediFilePath</c> run parameter overrides the <b>déductions</b> archive only — it lets an
/// operator re-send a specific main archive while any annexe the bridge produced is still sent, so an
/// override cannot silently drop one.</para>
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

        foreach (var archive in archives)
        {
            await TvaDom.WaitForAttachedAsync(ctx, input, "le champ de dépôt du fichier EDI", ct);

            var name = await AttachAsync(ctx, input, archive, ct);

            // The portal ships « Charger » disabled and enables it once it has accepted an attachment, so
            // this is its own confirmation that the file went in.
            await TvaDom.WaitForEnabledAsync(ctx, uploadButton, "le bouton « Charger »", ct);

            // Sending an EDI file registers a real deposit against the taxpayer's account (it shows up
            // under "Suivi Envoi EDI"), so the dry-run switch protects it too.
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
                // Deliberately explicit about what DID go through: the déductions archive may already be
                // filed while an annexe is not, which leaves an incomplete declaration on the portal.
                // That is a state a human has to resolve, not one to log and walk past.
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

    private static async Task<string> AttachAsync(RobotContext ctx, string input, Archive archive, CancellationToken ct)
    {
        try
        {
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
    /// Which archives to send and in which order, plus where each choice came from — the latter goes in
    /// the log, because "which file did it actually upload" is the first question asked when a filing
    /// looks wrong.
    /// </summary>
    private static List<Archive> Resolve(RobotContext ctx)
    {
        var payload = Payload(ctx);
        var archives = new List<Archive>();

        // An explicitly supplied path is an instruction, not a fallback.
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

    private sealed record Archive(string Path, string Kind, string Source);

    /// <summary>
    /// Reads the portal's answer to the upload. An error dialog fails the step. <b>No dialog at all does
    /// not</b> — refusing an upload that in fact succeeded would be worse than reporting it unverified,
    /// and the warning says which happened.
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
/// Reopens the declaration created earlier, so its amounts can be typed in. Opening a different
/// declaration than the one created fails here rather than being recalculated by the next step.
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

        var row = ctx.Portal.Element("declarationEditRow").Replace("{id}", id);
        var editButton = $"{row} {ctx.Portal.Element("declarationEditButton")}";

        if (await TvaDom.PresentAsync(ctx, editButton, ct))
        {
            ctx.Logger.LogInformation("Déclaration {Id} trouvée dans le tableau — ouverture en édition", id);
            await ctx.Page.ClickAsync(editButton, ct);
        }
        else
        {
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
/// Types the dossier's amounts into the open declaration form, the portal's equivalent of the desktop's
/// <c>pgDeclaration</c> screen.
///
/// <para><b>Only what the portal will accept is typed.</b> Within a row, the numeric cells are the left
/// (base / chiffre d'affaires) and right (taxe) columns in that order. A cell the portal ships locked is
/// one it derives itself, such as the "Chiffre d'affaires imposable" total or a tax computed from
/// base × taux; those are left alone, exactly as the desktop screen lets <c>Calcul.Recalculer</c> fill
/// them. Deriving them is what the next step's « Calculer » is for.</para>
///
/// <para><b>Every write is read back.</b> Anything that does not come back is collected, and the step
/// fails at the end with the complete list rather than at the first problem — a figure the form silently
/// dropped would otherwise be filed unnoticed, and one run then tells the operator about every line that
/// needs attention.</para>
///
/// <para>Zero amounts are skipped. The declaration was created fresh by this same robot (any pending one
/// was deleted first), so its fields start empty and empty already means zero.</para>
/// </summary>
public sealed class FillDeclarationAmountsStep : IRobotStep
{
    /// <summary>Money, so at most two decimals.</summary>
    private const string AmountFormat = "0.##";

    /// <summary>Half a centime: reformatting by the portal is accepted, an altered or dropped figure is
    /// not.</summary>
    private const double AmountTolerance = 0.005;

    private const int MaxSectionsToExpand = 40;

    /// <summary>
    /// Codes the legacy robot maps by hand: for these it writes the LEFT amount into the "TVA déductible"
    /// (right) column, instead of the right amount this step's general rule puts there. Not special-cased
    /// here — the general rule applies and a warning names the line, so the first real run answers whether
    /// the redesigned form still needs it.
    /// </summary>
    private static readonly HashSet<int> LegacyRightFromLeftCodes = [170, 180, 185, 186, 187];

    public string Name => "Saisie des montants de la déclaration";

    /// <summary>
    /// Not retried. The failures this step raises are mismatches between the dossier's lines and the
    /// form's rows — a replay cannot fix one, and each individual write is already verified here.
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
        // data — so it is not a failure by itself. What matters is the value in place, checked below
        // either way.
        if (await ctx.Page.IsDisabledAsync(selector, ct))
            ctx.Logger.LogInformation(
                "La liste « Fait Générateur » est verrouillée par le portail — sa valeur est vérifiée telle quelle");
        else
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
    /// Fills the prorata rate when the portal has a field for it. The value is logged whether or not it
    /// was typed, so a run whose prorata mattered can be told apart from one whose did not.
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

    private static async Task ExpandSectionsAsync(RobotContext ctx, CancellationToken ct)
    {
        foreach (var key in new[] { "declarationCollapsedSection", "declarationCollapsedTab" })
        {
            var selector = TvaDom.Optional(ctx, key);
            if (selector is null) continue;

            var opened = 0;
            var remaining = await ctx.Page.CountAsync(selector, ct);

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

    private static async Task FillLineAsync(
        RobotContext ctx, DeclarationLine line, Tally tally, CancellationToken ct)
    {
        var label = Describe(line);

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
    /// A row with a single numeric cell, which is three different situations — and telling them apart is
    /// what keeps a computed total from being mistaken for a field the robot failed to fill.
    ///
    /// <para><b>Greyed out.</b> The total lines print one cell that the portal computes: "TVA exigible"
    /// (132), "Total des déductions" (182), "Total de la TVA déductible" (190), "Crédit (190 - 132)" (201),
    /// "TVA due de la période" (205)… The dossier has a figure for each, but there is nothing to type —
    /// « Calculer » derives them from the lines above.</para>
    ///
    /// <para><b>Editable, with a left amount.</b> A left-only block — the portal's "A/ CA total", the
    /// desktop's <c>bgA</c>, which has no right column at all — so the cell is the left column and takes
    /// MntG. A right amount the calculation produced then has nowhere to go, which is normal here.</para>
    ///
    /// <para><b>Editable, with only a right amount.</b> One amount and one cell, so they pair up and the
    /// amount goes in — line 131, "Montant de la retenue à la source opérée par les clients", is this
    /// case. The row does not say whether its cell is a base or a tax column, but the dossier does: a
    /// base-only line would carry a left amount too, and this one carries none.</para>
    /// </summary>
    private static async Task FillSingleCellLineAsync(
        RobotContext ctx, DeclarationLine line, string label, string cell, Tally tally, CancellationToken ct)
    {
        // A cell that cannot be typed into settles the question: a line the portal computes cannot be
        // filled wrongly and cannot be missing.
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
        // block whose calculation produced a right value and no left one, and the figure landing in the
        // wrong column would then be invisible.
        ctx.Logger.LogWarning(
            "{Label} : cellule unique et saisissable — le montant de droite ({Value}) y est saisi. " +
            "Contrôlez cette ligne sur le formulaire après « Calculer ».", label, Amount(line.MntD));

        await WriteAsync(ctx, cell, line.MntD, $"{label}, montant", tally, ct);
    }

    /// <summary>
    /// Resolves a code to the one row that should receive its amounts. A code that resolves to several
    /// typeable rows is reported rather than guessed at: picking the wrong line of a tax return is worse
    /// than stopping.
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

        // A cell that cannot be typed into is the portal saying it derives this figure itself — a total,
        // or a tax it computes from base × taux. « Calculer » recomputes it.
        if (!await ctx.Page.IsEditableAsync(cell, ct))
        {
            tally.Computed++;
            ctx.Logger.LogDebug(
                "{What} : cellule calculée par le portail, {Value} non saisi", what, Amount(value));
            return;
        }

        var text = Amount(value);
        await ctx.Page.FillAsync(cell, text, ct);

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

    private static string Cell(string cells, int index) => Xpath($"({cells})[{index}]");

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

    private sealed record Row(string Expression, int Cells);

    /// <summary>What the pass over the lines produced. Problems are collected rather than thrown so one
    /// run reports every line that needs attention.</summary>
    private sealed class Tally
    {
        public int Written;
        public int Computed;
        public List<string> Problems { get; } = [];
    }
}

/// <summary>
/// Asks the portal to recalculate the declaration (« Calculer »), so the amounts just typed in and the
/// figures brought in by the EDI upload are consolidated into the totals. This is the portal's
/// counterpart of the desktop screen's « Actualiser », and it is what fills every cell
/// <see cref="FillDeclarationAmountsStep"/> deliberately left alone.
///
/// <para>Completion is reported, not asserted: the portal shows no dedicated "calculated" state, and a
/// recalculation that legitimately changes nothing would otherwise be reported as broken.</para>
/// </summary>
public sealed class RecalculateDeclarationStep : IRobotStep
{
    public string Name => "Recalcul de la déclaration";

    /// <summary>One recalculation per run is what is meant.</summary>
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

        await TvaDom.DismissDialogsAsync(ctx, ct);
    }
}

internal static class TvaMenu
{
    public static async Task GoToAsync(
        RobotContext ctx,
        string groupKey, string itemKey, string urlKey,
        string group, string item, string what,
        CancellationToken ct)
    {
        var menuToggle = ctx.Portal.Element("menuToggle");
        var menuGroup = ctx.Portal.Element(groupKey);
        var menuItem = ctx.Portal.Element(itemKey);

        await ctx.Page.ScrollToTopAsync(ct);

        if (!await TvaDom.ReachableAsync(ctx, menuItem, ct))
        {
            if (!await TvaDom.ReachableAsync(ctx, menuGroup, ct))
            {
                await TvaDom.WaitForAsync(ctx, menuToggle, "l'ouverture du menu (« MENU / القائمة »)", ct);
                await ctx.Page.ClickAsync(menuToggle, ct);
                await TvaDom.WaitForReachableAsync(ctx, menuGroup, $"{group} du menu ouvert", ct);
            }

            await ctx.Page.ClickAsync(menuGroup, ct);
            await TvaDom.WaitForReachableAsync(ctx, menuItem, $"{item} du sous-menu", ct);
        }

        await ctx.Page.ClickAsync(menuItem, ct);

        await TvaDom.WaitForUrlAsync(ctx, ctx.Portal.Element(urlKey), what, ct);

        ctx.Logger.LogInformation("Navigation vers {What} : {Url}", what, ctx.Page.Url);
    }
}

internal static class TvaDom
{
    public const int PollIntervalMs = 250;

    private const int DialogSettleMs = 2_000;

    public static string? Optional(RobotContext ctx, string key) =>
        ctx.Portal.Elements.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : null;

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

    public static async Task<bool> ReachableAsync(RobotContext ctx, string selector, CancellationToken ct) =>
        await ctx.Page.IsVisibleAsync(selector, ct) && await ctx.Page.IsInViewportAsync(selector, ct);

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

    public static async Task WaitForAttachedAsync(
        RobotContext ctx, string selector, string what, CancellationToken ct)
    {
        if (await Poll.UntilAsync(ctx, ctx.DefaultTimeoutMs, PollIntervalMs,
                async (c, token) => await c.Page.CountAsync(selector, token) > 0, ct))
            return;

        throw new InvalidOperationException(
            $"Impossible de trouver {what} (« {selector} ») dans la page. Page courante : {ctx.Page.Url}");
    }

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
    /// Waits out the dialog on screen and dismisses whatever the portal puts up next. Necessary rather
    /// than cosmetic: a popup left open makes every later click land on its backdrop instead of the app.
    /// </summary>
    public static async Task DismissDialogsAsync(RobotContext ctx, CancellationToken ct)
    {
        var dialog = Optional(ctx, "dialog");
        if (dialog is null) return;

        var confirm = Optional(ctx, "dialogConfirm");

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

    public static string? Normalize(string? text) =>
        string.IsNullOrWhiteSpace(text) ? null : Regex.Replace(text, @"\s+", " ").Trim();
}
