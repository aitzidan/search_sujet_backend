namespace RobotAutomation.Application.Configuration;

/// <summary>
/// How to reach the legacy data bridge (<c>GeneraFi.Robot.Bridge.exe</c>), bound from "Bridge" in
/// appsettings. The bridge is a 32-bit .NET Framework console app that reads a client's Access dossier
/// and prints the declaration as JSON; it is launched once per call and exits.
/// </summary>
public sealed class BridgeOptions
{
    public const string SectionName = "Bridge";

    /// <summary>
    /// Full path to <c>GeneraFi.Robot.Bridge.exe</c>. It lives in the GénéraFi TVA solution's build
    /// output, not in this repo, so it has to be configured per machine.
    /// </summary>
    public string? ExecutablePath { get; set; }

    /// <summary>
    /// How long the bridge may take. Generous on purpose: it opens an Access file and runs the whole VAT
    /// calculation, which on a large dossier is seconds-to-a-minute, not milliseconds.
    /// </summary>
    public int TimeoutMs { get; set; } = 120_000;

    /// <summary>
    /// Where the bridge writes the generated EDI archive. Environment variables are expanded, so
    /// <c>%APPDATA%\…</c> works. Blank falls back to <see cref="DefaultEdiDirectory"/>.
    ///
    /// This side of the boundary owns the location: the bridge writes only where it is told, and reports
    /// the path back. Each archive is one taxpayer's complete list of suppliers and invoices, and they are
    /// kept (the desktop app treats them as the télédéclaration audit trail), so the directory must stay
    /// outside every source tree.
    /// </summary>
    public string? EdiDirectory { get; set; }

    /// <summary>
    /// The GénéraFi desktop app's own declarations folder. Chosen as the default so the archives land where
    /// the application already writes them and where the accountant already looks for them.
    /// </summary>
    public const string DefaultEdiDirectory = @"%APPDATA%\GeneraFi\GeneraFi_TVA\Declarations";

    /// <summary>
    /// The configured directory with environment variables expanded, guaranteed absolute.
    ///
    /// The rooted check is not defensive padding: a blank or relative value would resolve against the API
    /// process's working directory — the repository — and that is exactly how run screenshots containing real
    /// session data ended up committed once already (<c>ScreenshotDirectory: null</c> → <c>""</c>). An EDI
    /// archive in a tracked folder is a worse version of the same accident, so this refuses rather than
    /// guesses.
    /// </summary>
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
