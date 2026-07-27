using Microsoft.Extensions.Logging;
using RobotAutomation.Application.Robots.Abstractions;

namespace RobotAutomation.Application.Robots.Steps;

// Robot 2 — proves automation BEYOND login: after authenticating, open the "Produits importés"
// screen and enter several product rows (désignation, pays, valeur en douane, taux, montant TVA),
// then validate. Demonstrates dynamic, multi-row data entry.

/// <summary>From the post-login menu, open the "Produits importés" screen.</summary>
public sealed class OpenImportedProductsStep : IRobotStep
{
    public string Name => "Ouverture des produits importés";

    public async Task ExecuteAsync(RobotContext ctx, CancellationToken ct)
    {
        await ctx.Page.ClickAsync(ctx.Portal.Element("menuProduits"), ct);
        await ctx.Page.WaitForSelectorAsync(ctx.Portal.Element("addProductButton"), ctx.DefaultTimeoutMs, ct);
    }
}

/// <summary>Add and fill each imported-product row (adding a new row before every product after the first).</summary>
public sealed class EnterImportedProductsStep : IRobotStep
{
    private static readonly ImportedProduct[] Products =
    [
        new("Ordinateurs portables", "Chine", "250000", "20", "50000"),
        new("Téléphones mobiles", "Corée du Sud", "120000", "20", "24000"),
        new("Imprimantes laser", "Allemagne", "80000", "20", "16000"),
    ];

    public string Name => "Saisie des produits importés";

    public async Task ExecuteAsync(RobotContext ctx, CancellationToken ct)
    {
        var rowPrefixTemplate = ctx.Portal.Element("productRowPrefix");
        var addButton = ctx.Portal.Element("addProductButton");

        for (var i = 0; i < Products.Length; i++)
        {
            if (i > 0) await ctx.Page.ClickAsync(addButton, ct);

            var rowPrefix = rowPrefixTemplate.Replace("{i}", i.ToString());
            string field(string key) => $"{rowPrefix} {ctx.Portal.Element(key)}";

            await ctx.Page.WaitForSelectorAsync(field("productDesignation"), ctx.DefaultTimeoutMs, ct);
            await ctx.Page.FillAsync(field("productDesignation"), Products[i].Designation, ct);
            await ctx.Page.FillAsync(field("productPays"), Products[i].Pays, ct);
            await ctx.Page.FillAsync(field("productValeur"), Products[i].ValeurDouane, ct);
            await ctx.Page.FillAsync(field("productTaux"), Products[i].TauxTva, ct);
            await ctx.Page.FillAsync(field("productMontant"), Products[i].MontantTva, ct);
        }

        ctx.Logger.LogInformation("Entered {Count} imported products", Products.Length);
    }

    private sealed record ImportedProduct(
        string Designation, string Pays, string ValeurDouane, string TauxTva, string MontantTva);
}

/// <summary>Validate the imported-products entry. Non-retryable to avoid double submission.</summary>
public sealed class SubmitImportedProductsStep : IRobotStep
{
    public string Name => "Validation des produits";
    public bool Retryable => false;

    public async Task ExecuteAsync(RobotContext ctx, CancellationToken ct)
    {
        await ctx.Page.ClickAsync(ctx.Portal.Element("submitProductsButton"), ct);
    }
}

/// <summary>Confirm the products were saved.</summary>
public sealed class VerifyImportedProductsStep : IRobotStep
{
    public string Name => "Vérification des produits";

    public async Task ExecuteAsync(RobotContext ctx, CancellationToken ct)
    {
        var indicator = ctx.Portal.Element("productsSuccessIndicator");
        await ctx.Page.WaitForSelectorAsync(indicator, ctx.DefaultTimeoutMs, ct);
        if (!await ctx.Page.IsVisibleAsync(indicator, ct))
            throw new InvalidOperationException("Imported-products confirmation did not appear.");
        ctx.Logger.LogInformation("Imported products saved and confirmed");
    }
}
