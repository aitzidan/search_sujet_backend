using Microsoft.Extensions.Logging;
using RobotAutomation.Application.Files;
using RobotAutomation.Application.Robots.Abstractions;

namespace RobotAutomation.Application.Robots.Steps;

// Robot 1 — the télédéclaration flow, mirroring the legacy Etape sequence
// (open menu → create period → upload EDI → fill declaration → submit → confirm),
// executed after the shared login steps. Selectors come from DgiPortalOptions.Elements.

/// <summary>From the post-login menu, open the "Déclaration TVA" screen.</summary>
public sealed class OpenDeclarationStep : IRobotStep
{
    public string Name => "Ouverture de la déclaration";

    public async Task ExecuteAsync(RobotContext ctx, CancellationToken ct)
    {
        await ctx.Page.ClickAsync(ctx.Portal.Element("menuDeclaration"), ct);
        await ctx.Page.WaitForSelectorAsync(ctx.Portal.Element("regimeSelect"), ctx.DefaultTimeoutMs, ct);
    }
}

/// <summary>Choose régime + mois + année and create the declaration period (legacy CreationPeriode).</summary>
public sealed class CreatePeriodStep : IRobotStep
{
    public string Name => "Création de la période";

    public async Task ExecuteAsync(RobotContext ctx, CancellationToken ct)
    {
        await ctx.Page.SelectOptionAsync(ctx.Portal.Element("regimeSelect"), "M", ct);   // Mensuel
        await ctx.Page.SelectOptionAsync(ctx.Portal.Element("moisSelect"), "1", ct);      // Janvier
        await ctx.Page.SelectOptionAsync(ctx.Portal.Element("anneeSelect"), "2026", ct);
        await ctx.Page.ClickAsync(ctx.Portal.Element("createPeriodButton"), ct);
        await ctx.Page.WaitForSelectorAsync(ctx.Portal.Element("caTotalInput"), ctx.DefaultTimeoutMs, ct);
        ctx.Logger.LogInformation("Period created (régime=M, mois=1, année=2026)");
    }
}

/// <summary>Attach the EDI file via SetInputFiles — the modern replacement for the legacy SendKeys upload.</summary>
public sealed class UploadEdiFileStep : IRobotStep
{
    private readonly ISampleFileProvider _files;

    public UploadEdiFileStep(ISampleFileProvider files) => _files = files;

    public string Name => "Dépôt du fichier EDI";

    public async Task ExecuteAsync(RobotContext ctx, CancellationToken ct)
    {
        var path = await _files.GetSampleEdiFileAsync(ct);
        await ctx.Page.SetInputFilesAsync(ctx.Portal.Element("ediFileInput"), path, ct);
        ctx.Logger.LogInformation("EDI file attached: {File}", Path.GetFileName(path));
    }
}

/// <summary>Fill the TVA recap amounts (legacy RemplireEtat / editTva grid, simplified).</summary>
public sealed class FillDeclarationStep : IRobotStep
{
    public string Name => "Remplissage du récapitulatif TVA";

    public async Task ExecuteAsync(RobotContext ctx, CancellationToken ct)
    {
        await ctx.Page.FillAsync(ctx.Portal.Element("caTotalInput"), "1000000", ct);
        await ctx.Page.FillAsync(ctx.Portal.Element("tvaExigibleInput"), "200000", ct);
        await ctx.Page.FillAsync(ctx.Portal.Element("tvaDeductibleInput"), "50000", ct);
    }
}

/// <summary>Submit the declaration (legacy SoumettrePourValidation). Non-retryable to avoid double submit.</summary>
public sealed class SubmitDeclarationStep : IRobotStep
{
    public string Name => "Soumission de la déclaration";
    public bool Retryable => false;

    public async Task ExecuteAsync(RobotContext ctx, CancellationToken ct)
    {
        await ctx.Page.ClickAsync(ctx.Portal.Element("submitDeclarationButton"), ct);
    }
}

/// <summary>Confirm the declaration was accepted.</summary>
public sealed class VerifyDeclarationStep : IRobotStep
{
    public string Name => "Vérification de la déclaration";

    public async Task ExecuteAsync(RobotContext ctx, CancellationToken ct)
    {
        var indicator = ctx.Portal.Element("declarationSuccessIndicator");
        await ctx.Page.WaitForSelectorAsync(indicator, ctx.DefaultTimeoutMs, ct);
        if (!await ctx.Page.IsVisibleAsync(indicator, ct))
            throw new InvalidOperationException("Declaration confirmation did not appear.");
        ctx.Logger.LogInformation("Declaration submitted and confirmed");
    }
}
