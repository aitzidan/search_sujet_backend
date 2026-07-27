using Microsoft.Extensions.Logging;
using RobotAutomation.Application.Robots.Abstractions;

namespace RobotAutomation.Application.Robots.Steps;

// Robot 3 — books an appointment on the REAL DGI rendez-vous portal (tax.rdv.gov.ma).
// Every selector comes from DgiPortalOptions.Elements (the "rdv" config) so the flow can be tuned
// against the live DOM without recompiling. Dropdowns are selected BY LABEL (robust against PRADO's
// generated ids). The final booking is guarded by DgiPortalOptions.StopBeforeFinalSubmit (dry-run).

/// <summary>From the home page, open the "Prendre un rendez-vous" wizard.</summary>
public sealed class OpenRendezVousStep : IRobotStep
{
    public string Name => "Ouverture de la prise de rendez-vous";

    public async Task ExecuteAsync(RobotContext ctx, CancellationToken ct)
    {
        await ctx.Page.ClickAsync(ctx.Portal.Element("takeAppointmentLink"), ct);
        await ctx.Page.WaitForSelectorAsync(ctx.Portal.Element("regionSelect"), ctx.DefaultTimeoutMs, ct);
    }
}

/// <summary>
/// Étape 1 — choose the prestation. Each dropdown selection fires a PRADO AJAX callback that toggles
/// a loader and repopulates the next control, so we wait for the loader to settle between selections.
/// Selects are chosen by label (forced, since the Direction select is hidden behind the Chosen plugin).
/// Order matters: région → direction → nature → vous êtes → lieu d'imposition.
/// </summary>
public sealed class SelectPrestationStep : IRobotStep
{
    public string Name => "Choix de la prestation";

    /// <summary>Fill attempt + up to 2 repairs before giving up on a field the portal keeps resetting.</summary>
    private const int MaxRepairAttempts = 3;

    private static readonly (string SelectKey, string ParamKey)[] Fields =
    {
        ("regionSelect", "region"),
        ("directionSelect", "direction"),
        ("natureSelect", "nature"),
        ("vousEtesSelect", "vousEtes"),
        ("lieuImpositionSelect", "lieuImposition"),
    };

    public async Task ExecuteAsync(RobotContext ctx, CancellationToken ct)
    {
        var applicable = Fields.Where(f => !string.IsNullOrWhiteSpace(ctx.GetParameter(f.ParamKey))).ToList();

        foreach (var field in applicable)
            await SelectIfProvidedAsync(ctx, field.SelectKey, field.ParamKey, ct);

        var cguSelector = ctx.Portal.Element("cguCheckbox");
        await ctx.Page.ClickAsync(cguSelector, ct);

        await VerifyAndRepairAsync(ctx, applicable, cguSelector, ct);

        await ctx.Page.ClickAsync(ctx.Portal.Element("nextStepButton"), ct);    // Etape suivante
    }

    /// <summary>
    /// PRADO's callbacks can re-render an entire form panel (not just the field that changed), which can
    /// silently reset an EARLIER dropdown back to its placeholder while a LATER field's AJAX response is
    /// still landing. Re-check every field right before the final click and reapply anything that reverted
    /// — advancing with an incomplete form doesn't fail loudly here, it surfaces downstream as an empty
    /// calendar at étape 2 (the server can't resolve which office/service to compute slots for).
    /// </summary>
    private static async Task VerifyAndRepairAsync(
        RobotContext ctx, List<(string SelectKey, string ParamKey)> applicable, string cguSelector, CancellationToken ct)
    {
        for (var attempt = 1; attempt <= MaxRepairAttempts; attempt++)
        {
            var reverted = new List<(string SelectKey, string ParamKey)>();
            foreach (var field in applicable)
                if (!await ctx.Page.HasSelectedOptionAsync(ctx.Portal.Element(field.SelectKey), ct))
                    reverted.Add(field);

            var cguChecked = await ctx.Page.IsCheckedAsync(cguSelector, ct);

            if (reverted.Count == 0 && cguChecked)
                return;

            if (attempt == MaxRepairAttempts)
            {
                var what = reverted.Count > 0 ? string.Join(", ", reverted.Select(f => f.SelectKey)) : "les CGU";
                throw new InvalidOperationException(
                    $"Le portail a réinitialisé {what} après {MaxRepairAttempts} tentatives — impossible de stabiliser le formulaire.");
            }

            ctx.Logger.LogWarning(
                "Portail : {Reverted} champ(s) réinitialisé(s), CGU {Cgu} — nouvelle tentative ({Attempt}/{Max})",
                reverted.Count, cguChecked ? "OK" : "à recocher", attempt, MaxRepairAttempts);

            foreach (var field in reverted)
                await SelectIfProvidedAsync(ctx, field.SelectKey, field.ParamKey, ct);
            if (!cguChecked)
                await ctx.Page.ClickAsync(cguSelector, ct);
        }
    }

    private static async Task SelectIfProvidedAsync(RobotContext ctx, string selectKey, string paramKey, CancellationToken ct)
    {
        var value = ctx.GetParameter(paramKey);
        if (string.IsNullOrWhiteSpace(value))
        {
            ctx.Logger.LogInformation("Skipping {Field} — no '{Param}' parameter provided", selectKey, paramKey);
            return;
        }

        await ctx.Page.SelectOptionByLabelAsync(ctx.Portal.Element(selectKey), value!, ct);
        await AjaxWait.ForLoaderAsync(ctx, ct);
    }
}

/// <summary>Shared best-effort wait for a PRADO callback to complete (the <c>#bloc-loader</c> spinner shows
/// then hides) — used after any action that triggers a partial postback: dependent-dropdown selection
/// (étape 1) and calendar-day selection, which reloads the time-slot panel (étape 2).</summary>
internal static class AjaxWait
{
    public static async Task ForLoaderAsync(RobotContext ctx, CancellationToken ct)
    {
        if (!ctx.Portal.Elements.TryGetValue("loader", out var loader) || string.IsNullOrWhiteSpace(loader))
            return;

        try { await ctx.Page.WaitForSelectorAsync(loader, 1500, ct); }
        catch (OperationCanceledException) { throw; }
        catch { /* loader may not have shown — fine */ }

        try { await ctx.Page.WaitForHiddenAsync(loader, ctx.DefaultTimeoutMs, ct); }
        catch (OperationCanceledException) { throw; }
        catch { /* already settled — fine */ }
    }
}

/// <summary>Étape 2 — pick the first available date and time slot. Fails clearly if none are available.</summary>
public sealed class ChooseSlotStep : IRobotStep
{
    public string Name => "Choix du créneau";

    public async Task ExecuteAsync(RobotContext ctx, CancellationToken ct)
    {
        var availableDate = ctx.Portal.Element("availableDate");
        await WaitOrFailAsync(ctx, availableDate, "Aucune date disponible dans le calendrier.", ct);
        if (await ctx.Page.CountAsync(availableDate, ct) == 0)
            throw new InvalidOperationException("Aucune date disponible dans le calendrier.");
        await ctx.Page.ClickAsync(availableDate, ct); // first available date
        await AjaxWait.ForLoaderAsync(ctx, ct);        // selecting a date reloads the time-slot panel

        var timeSlot = ctx.Portal.Element("timeSlot");
        await WaitOrFailAsync(ctx, timeSlot, "Aucun créneau horaire disponible pour cette date.", ct);
        if (await ctx.Page.CountAsync(timeSlot, ct) == 0)
            throw new InvalidOperationException("Aucun créneau horaire disponible pour cette date.");
        await ctx.Page.ClickAsync(timeSlot, ct); // first available slot

        await ctx.Page.ClickAsync(ctx.Portal.Element("nextStepButton"), ct);
        await ctx.Page.WaitForSelectorAsync(ctx.Portal.Element("iceInput"), ctx.DefaultTimeoutMs, ct);
    }

    private static async Task WaitOrFailAsync(RobotContext ctx, string selector, string friendly, CancellationToken ct)
    {
        try { await ctx.Page.WaitForSelectorAsync(selector, ctx.DefaultTimeoutMs, ct); }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { throw new InvalidOperationException(friendly, ex); }
    }
}

/// <summary>Étape 3 — fill the applicant details (ICE, IF/CNI/CS, raison sociale, adresse, email, téléphone).</summary>
public sealed class FillValidationStep : IRobotStep
{
    public string Name => "Saisie des informations";

    public async Task ExecuteAsync(RobotContext ctx, CancellationToken ct)
    {
        await FillAsync(ctx, "iceInput", "ice", ct);
        await FillAsync(ctx, "identifiantFiscalInput", "identifiantFiscal", ct);
        await FillAsync(ctx, "raisonSocialeInput", "raisonSociale", ct);
        await FillAsync(ctx, "adresseInput", "adresse", ct);
        await FillAsync(ctx, "emailInput", "email", ct);
        await FillAsync(ctx, "telephoneInput", "telephone", ct);
    }

    private static async Task FillAsync(RobotContext ctx, string selectorKey, string paramKey, CancellationToken ct)
    {
        var value = ctx.GetParameter(paramKey) ?? "";
        await ctx.Page.FillAsync(ctx.Portal.Element(selectorKey), value, ct);
    }
}

/// <summary>
/// Confirm the booking. Non-retryable (never double-book). Honours the StopBeforeFinalSubmit
/// dry-run switch: when on, everything above has run but this step does NOT click
/// "Valider votre réservation", so no real appointment is created.
/// </summary>
public sealed class ConfirmRendezVousStep : IRobotStep
{
    public string Name => "Confirmation du rendez-vous";
    public bool Retryable => false;

    public async Task ExecuteAsync(RobotContext ctx, CancellationToken ct)
    {
        if (ctx.Portal.StopBeforeFinalSubmit)
        {
            ctx.Output["dryRun"] = "true";
            ctx.Logger.LogWarning("DRY-RUN: form filled but 'Valider votre réservation' NOT clicked (no real appointment created)");
            return;
        }

        var confirmButton = ctx.Portal.Element("confirmButton");
        await ctx.Page.ClickAsync(confirmButton, ct);

        // The real button hides itself immediately via its own inline onclick handler — a confirmed,
        // evidence-based signal that the submit fired, rather than guessing at the post-submit page's
        // (still unknown) markup.
        await ctx.Page.WaitForHiddenAsync(confirmButton, ctx.DefaultTimeoutMs, ct);

        // Extra check, only if a real confirmation-page selector has since been configured.
        if (ctx.Portal.Elements.TryGetValue("confirmationReady", out var ready) && !string.IsNullOrWhiteSpace(ready))
            await ctx.Page.WaitForSelectorAsync(ready, ctx.DefaultTimeoutMs, ct);
    }
}

/// <summary>Étape 4 — read the confirmation details and return them as run output. Skipped in dry-run.</summary>
public sealed class CaptureConfirmationStep : IRobotStep
{
    public string Name => "Récupération de la confirmation";

    public async Task ExecuteAsync(RobotContext ctx, CancellationToken ct)
    {
        if (ctx.Portal.StopBeforeFinalSubmit)
        {
            ctx.Logger.LogInformation("DRY-RUN: no confirmation page to capture");
            return;
        }

        var dateRdv = await SafeTextAsync(ctx, "confDate", ct);

        ctx.Output["confirmationNumber"] = await SafeTextAsync(ctx, "confNumber", ct);
        ctx.Output["date"] = dateRdv;
        ctx.Output["heure"] = ExtractHeure(dateRdv);
        ctx.Output["direction"] = await SafeTextAsync(ctx, "confDirection", ct);
        ctx.Output["nature"] = await SafeTextAsync(ctx, "confNature", ct);
        ctx.Logger.LogInformation("Captured confirmation number {Number}", ctx.Output["confirmationNumber"]);
    }

    private static async Task<string?> SafeTextAsync(RobotContext ctx, string selectorKey, CancellationToken ct)
    {
        if (!ctx.Portal.Elements.TryGetValue(selectorKey, out var selector) || string.IsNullOrWhiteSpace(selector))
            return null;
        return await ctx.Page.GetTextAsync(selector, ct);
    }

    /// <summary>The portal shows date and time combined in one span ("Lundi 27/07/2026 à 09:10") — there is
    /// no separate "heure" element, so split it out of the date text instead of depending on a selector
    /// that doesn't exist.</summary>
    private static string? ExtractHeure(string? dateRdv)
    {
        if (string.IsNullOrWhiteSpace(dateRdv)) return null;
        var idx = dateRdv.LastIndexOf(" à ", StringComparison.Ordinal);
        return idx >= 0 ? dateRdv[(idx + 3)..].Trim() : null;
    }
}
