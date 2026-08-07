namespace RobotAutomation.Application.Configuration;

public sealed class BridgeOptions
{
    public const string SectionName = "Bridge";

    public string? ExecutablePath { get; set; }

    public int TimeoutMs { get; set; } = 120_000;

    /// <summary>
    /// Where the generated EDI archives are written. Each archive is one taxpayer's complete list of
    /// suppliers and invoices, and they are kept as the télédéclaration audit trail, so the directory
    /// must stay outside every source tree.
    /// </summary>
    public string? EdiDirectory { get; set; }

    /// <summary>The GénéraFi desktop app's own declarations folder, where the accountant already looks
    /// for them.</summary>
    public const string DefaultEdiDirectory = @"%APPDATA%\GeneraFi\GeneraFi_TVA\Declarations";

    public string ResolveEdiDirectory()
    {
        var configured = string.IsNullOrWhiteSpace(EdiDirectory) ? DefaultEdiDirectory : EdiDirectory;
        var expanded = Environment.ExpandEnvironmentVariables(configured).Trim();

        if (!Path.IsPathRooted(expanded))
            throw new InvalidOperationException(
                $"Bridge:EdiDirectory doit être un chemin absolu : « {configured} » donne « {expanded} ». " +
                "Une variable d'environnement inconnue reste littérale, et un chemin relatif écrirait les " +
                "archives de déclaration dans le dossier de travail de l'API.");

        return expanded;
    }
}
