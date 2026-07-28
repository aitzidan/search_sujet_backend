using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using RobotAutomation.Application.Robots.Abstractions;

namespace RobotAutomation.Application.Robots.Steps;

// The declaration half of Robot 4 (DgiTvaRobot) on the REAL TVA portal (tva.tax.gov.ma). These steps run
// once the same robot's login steps (ManualLoginSteps) have authenticated the operator and the portal has
// landed on its home route "#/".
//
// Every selector comes from DgiPortalOptions.Elements (the "real" section) so the flow can be retuned
// against the live DOM without recompiling. Selectors deliberately anchor on LIBRARY markup — the
// <ng2-smart-table> tag, FontAwesome's .fa-remove, SweetAlert2's .swal2-* classes — never on Angular's
// generated _ngcontent-*/_nghost-* attributes, which change on every build.
//
// One entry in that map is expected TEXT rather than a selector: "declarationDeleteDialogText" is the
// wording a delete confirmation must contain before the robot is willing to confirm it.

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
    private const int PollIntervalMs = 250;

    /// <summary>How long a dialog is given to finish closing. Short on purpose: it is an animation, and
    /// a dialog still up after this is a *different* dialog to dismiss, not a slow one.</summary>
    private const int DialogSettleMs = 2_000;

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

        await WaitForTableAsync(ctx, table, ct);

        var pending = await WaitForDeclarationsAsync(ctx, row, deleteButton, ct);
        if (pending == 0)
        {
            ctx.Logger.LogInformation("Aucune déclaration dans le tableau — rien à supprimer");
            ctx.Output["declarationsSupprimees"] = "0";
            return;
        }

        ctx.Logger.LogInformation("{Count} déclaration(s) supprimable(s) détectée(s)", pending);

        // Same safety switch the rendez-vous robot uses for its final booking: in dry-run the robot goes
        // through the flow but performs no irreversible write. The steps that follow will then run against
        // a portal that still holds a pending declaration — expected, and logged loudly here.
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

    /// <summary>Waits for the declarations table to render; a miss is reported with the page the browser is
    /// actually on, since the usual cause is the portal not having reached its home route.</summary>
    private static async Task WaitForTableAsync(RobotContext ctx, string table, CancellationToken ct)
    {
        try
        {
            await ctx.Page.WaitForSelectorAsync(table, ctx.DefaultTimeoutMs, ct);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"Le tableau des déclarations (« {table} ») n'est pas apparu. Page courante : {ctx.Page.Url}", ex);
        }
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
        var dialog = ctx.Portal.Element("declarationDeleteDialog");
        var confirmButton = ctx.Portal.Element("declarationDeleteConfirm");
        var dialogTitle = ctx.Portal.Element("declarationDeleteDialogTitle");

        // Log WHAT is about to be deleted before deleting it: the run's step log is the only trace that
        // survives the operation.
        ctx.Logger.LogInformation(
            "Suppression de la déclaration : {Row}", Normalize(await ctx.Page.GetTextAsync(row, ct)) ?? "(ligne illisible)");

        await ctx.Page.ClickAsync(deleteButton, ct);

        try
        {
            await ctx.Page.WaitForSelectorAsync(dialog, ctx.DefaultTimeoutMs, ct);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"La boîte de confirmation (« {dialog} ») n'est pas apparue après le clic sur l'icône de suppression.", ex);
        }

        var title = Normalize(await ctx.Page.GetTextAsync(dialogTitle, ct));

        // Guard against confirming the wrong dialog: .swal2-confirm is whatever SweetAlert2 has open, and
        // the portal uses SweetAlert2 for errors too. If the wording does not look like a delete
        // confirmation, stop rather than click "Oui !" on something unknown.
        if (ctx.Portal.Elements.TryGetValue("declarationDeleteDialogText", out var expected)
            && !string.IsNullOrWhiteSpace(expected)
            && title?.Contains(expected, StringComparison.OrdinalIgnoreCase) != true)
            throw new InvalidOperationException(
                $"La boîte de dialogue affichée (« {title ?? "sans titre"} ») ne ressemble pas à une confirmation " +
                $"de suppression (« {expected} » attendu) — confirmation annulée par sécurité.");

        ctx.Logger.LogInformation("Confirmation demandée par le portail : {Title}", title);
        await ctx.Page.ClickAsync(confirmButton, ct);

        await DismissDialogsAsync(ctx, dialog, confirmButton, ct);

        // The authoritative signal: the table has one row fewer. A success notification is not enough —
        // it can be shown while the grid still holds the stale row.
        var removed = await Poll.UntilAsync(
            ctx, ctx.DefaultTimeoutMs, PollIntervalMs,
            async (c, token) => await c.Page.CountAsync(row, token) < before, ct);

        if (!removed)
            throw new InvalidOperationException(
                $"La suppression n'a pas abouti : le tableau contient toujours {before} déclaration(s) " +
                "après la confirmation.");

        ctx.Logger.LogInformation("Suppression confirmée — la ligne a disparu du tableau");
    }

    /// <summary>
    /// Waits out the confirmation dialog and dismisses whatever the portal puts up next (typically a
    /// "Supprimé !" result popup). Necessary rather than cosmetic: SweetAlert2's backdrop covers the page,
    /// so a popup left open makes every later click land on the overlay instead of the app.
    /// </summary>
    private static async Task DismissDialogsAsync(
        RobotContext ctx, string dialog, string confirmButton, CancellationToken ct)
    {
        // Bounded: two popups is the realistic maximum (confirmation + result). More than that means
        // something unexpected is on screen, and the row-count check below will report it.
        for (var i = 0; i < 2; i++)
        {
            var closed = await Poll.UntilAsync(
                ctx, DialogSettleMs, PollIntervalMs,
                async (c, token) => !await c.Page.IsVisibleAsync(dialog, token), ct);
            if (closed) return;

            ctx.Logger.LogInformation(
                "Notification du portail : {Message}", Normalize(await ctx.Page.GetTextAsync(dialog, ct)));

            if (!await ctx.Page.IsVisibleAsync(confirmButton, ct)) return;   // nothing to click it away with
            await ctx.Page.ClickAsync(confirmButton, ct);
        }
    }

    /// <summary>Collapses the whitespace an HTML table's text content is full of, so a logged row reads as
    /// one line.</summary>
    private static string? Normalize(string? text) =>
        string.IsNullOrWhiteSpace(text) ? null : Regex.Replace(text, @"\s+", " ").Trim();
}
