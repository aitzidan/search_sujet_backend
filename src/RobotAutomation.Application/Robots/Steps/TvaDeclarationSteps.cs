using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using RobotAutomation.Application.Robots.Abstractions;

namespace RobotAutomation.Application.Robots.Steps;

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

        await TvaDom.WaitForAsync(ctx, table, "le tableau des déclarations", ct);

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
        try
        {
            await ctx.Page.WaitForSelectorAsync(deleteButton, ctx.DefaultTimeoutMs, ct);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return 0;   // no delete icon within the full timeout => the table is loaded and empty
        }

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

    public async Task ExecuteAsync(RobotContext ctx, CancellationToken ct)
    {
        var menuToggle = ctx.Portal.Element("menuToggle");
        var menuGroup = ctx.Portal.Element("menuDeclarationGroup");
        var menuItem = ctx.Portal.Element("menuDeclarationCurrentPeriod");

        // Reachability, not visibility, decides whether a level still has to be opened — see
        // TvaDom.ReachableAsync: the folded nav is rendered off-canvas, so a visibility probe would report
        // this menu as already open and skip every click.
        if (!await TvaDom.ReachableAsync(ctx, menuItem, ct))
        {
            if (!await TvaDom.ReachableAsync(ctx, menuGroup, ct))
            {
                await TvaDom.WaitForAsync(ctx, menuToggle, "l'ouverture du menu (« MENU / القائمة »)", ct);
                await ctx.Page.ClickAsync(menuToggle, ct);
                await TvaDom.WaitForReachableAsync(
                    ctx, menuGroup, "le groupe « Déclarations du chiffre d'affaires » du menu ouvert", ct);
            }

            // Opens the sub-list. Clicking also hovers first, so this works whether the portal expands the
            // sub-menu on click or on hover.
            await ctx.Page.ClickAsync(menuGroup, ct);
            await TvaDom.WaitForReachableAsync(
                ctx, menuItem, "l'entrée « Déclaration Période en cours » du sous-menu", ct);
        }

        await ctx.Page.ClickAsync(menuItem, ct);

        // The hash route changing is the portal's own confirmation that the section was entered — a more
        // meaningful signal than any element of the page that follows.
        await TvaDom.WaitForUrlAsync(
            ctx, ctx.Portal.Element("declarationPageUrl"), "la page des déclarations", ct);

        ctx.Logger.LogInformation("Page de déclaration ouverte : {Url}", ctx.Page.Url);
    }
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

        var regimeSelect = await ResolveRegimeSelectAsync(ctx, ct);
        ctx.Logger.LogInformation("Régime demandé : {Regime}", regime);
        await ctx.Page.SelectOptionByLabelAsync(regimeSelect, regime, ct);

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
    /// The régime dropdown is the one control on this page located by a guessed id, so a miss falls back to
    /// "the first &lt;select&gt; on the page" instead of failing outright — and says so, loudly, because the
    /// fallback is a coincidence waiting to break.
    /// </summary>
    private static async Task<string> ResolveRegimeSelectAsync(RobotContext ctx, CancellationToken ct)
    {
        var primary = ctx.Portal.Element("declarationRegimeSelect");
        if (await ctx.Page.CountAsync(primary, ct) > 0) return primary;

        var fallback = TvaDom.Optional(ctx, "declarationRegimeSelectFallback");
        if (fallback is not null && await ctx.Page.CountAsync(fallback, ct) > 0)
        {
            ctx.Logger.LogWarning(
                "Le sélecteur de régime « {Primary} » est absent de la page — repli sur « {Fallback} ». " +
                "Corrigez declarationRegimeSelect dans la configuration.", primary, fallback);
            return fallback;
        }

        return primary;   // let SelectOptionByLabelAsync report which options the page actually offers
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
/// No scrolling code is needed: Playwright scrolls an element into view before clicking it. What does need
/// care is proving the save actually landed — the step watches for SweetAlert2's own success/error icon
/// classes, so it tells an accepted save from a rejected one without depending on the bilingual wording,
/// and refuses to continue on an unconfirmed save rather than carry a half-saved declaration into the
/// steps that follow.
/// </summary>
public sealed class SaveDeclarationStep : IRobotStep
{
    public string Name => "Enregistrement de la déclaration";

    /// <summary>Not idempotent — a retry would submit the form a second time.</summary>
    public bool Retryable => false;

    public async Task ExecuteAsync(RobotContext ctx, CancellationToken ct)
    {
        var saveButton = ctx.Portal.Element("declarationSaveButton");

        await TvaDom.WaitForAsync(ctx, saveButton, "le bouton « Enregistrer »", ct);
        await ctx.Page.ClickAsync(saveButton, ct);

        await TvaDom.WaitForLoaderAsync(ctx, ct);
        await VerifySavedAsync(ctx, ct);
    }

    /// <summary>
    /// Waits for one of three answers from the portal: an error dialog (fail now, with its wording), a
    /// success dialog (done), or a dialog still asking for a confirmation — which is the one thing that can
    /// keep both from appearing, so it is answered once and the watch continues.
    ///
    /// Blank out <c>dialogSuccess</c> in configuration to skip the check on a portal that saves silently.
    /// </summary>
    private static async Task VerifySavedAsync(RobotContext ctx, CancellationToken ct)
    {
        var success = TvaDom.Optional(ctx, "dialogSuccess");
        if (success is null)
        {
            ctx.Logger.LogWarning(
                "Aucun indicateur de succès configuré (dialogSuccess) — enregistrement NON vérifié");
            return;
        }

        var error = TvaDom.Optional(ctx, "dialogError");
        var confirm = TvaDom.Optional(ctx, "dialogConfirm");
        var confirmsLeft = 1;

        var confirmed = await Poll.UntilAsync(ctx, ctx.DefaultTimeoutMs, TvaDom.PollIntervalMs,
            async (c, token) =>
            {
                if (error is not null && await c.Page.IsVisibleAsync(error, token))
                    throw new InvalidOperationException(
                        "Le portail a refusé l'enregistrement : " +
                        $"« {await TvaDom.DialogTextAsync(c, token) ?? "sans message"} »");

                if (await c.Page.IsVisibleAsync(success, token)) return true;

                if (confirmsLeft > 0 && confirm is not null && await c.Page.IsVisibleAsync(confirm, token))
                {
                    confirmsLeft--;
                    c.Logger.LogInformation(
                        "Confirmation demandée avant enregistrement : {Message}",
                        await TvaDom.DialogTextAsync(c, token));
                    await c.Page.ClickAsync(confirm, token);
                }

                return false;
            }, ct);

        if (!confirmed)
            throw new InvalidOperationException(
                $"L'enregistrement n'a pas été confirmé : aucun message de succès (« {success} ») n'est " +
                $"apparu dans le délai imparti. Page courante : {ctx.Page.Url}");

        ctx.Logger.LogInformation(
            "Enregistrement confirmé par le portail : {Message}", await TvaDom.DialogTextAsync(ctx, ct));

        // Clear the popup so its backdrop cannot swallow the next step's first click.
        await TvaDom.DismissDialogsAsync(ctx, ct);
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
