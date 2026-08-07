using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RobotAutomation.Application.Configuration;
using RobotAutomation.Application.Declarations;

namespace RobotAutomation.Infrastructure.Declarations;

internal sealed class BridgeDeclarationDataSource : IDeclarationDataSource
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
    };

    private readonly BridgeOptions _options;
    private readonly ILogger<BridgeDeclarationDataSource> _logger;

    public BridgeDeclarationDataSource(
        IOptions<BridgeOptions> options,
        ILogger<BridgeDeclarationDataSource> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public async Task<DeclarationPayload> GetAsync(string dossierPath, int? periodeId, CancellationToken ct)
    {
        var exe = _options.ExecutablePath;
        if (string.IsNullOrWhiteSpace(exe))
            throw new InvalidOperationException(
                "Le pont vers les données GénéraFi n'est pas configuré : renseignez Bridge:ExecutablePath " +
                "(chemin de GeneraFi.Robot.Bridge.exe) dans appsettings.");
        if (!File.Exists(exe))
            throw new InvalidOperationException(
                $"GeneraFi.Robot.Bridge.exe est introuvable à « {exe} ». Compilez la solution GénéraFi TVA " +
                "puis corrigez Bridge:ExecutablePath.");

        var start = new ProcessStartInfo(exe)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        start.ArgumentList.Add("declaration-payload");
        start.ArgumentList.Add("--dossier");
        start.ArgumentList.Add(dossierPath);
        if (periodeId.HasValue)
        {
            start.ArgumentList.Add("--periode");
            start.ArgumentList.Add(periodeId.Value.ToString());
        }

        var ediDirectory = _options.ResolveEdiDirectory();
        start.ArgumentList.Add("--edi-dir");
        start.ArgumentList.Add(ediDirectory);

        _logger.LogInformation(
            "Lecture des données de déclaration pour le dossier {Dossier} (archive EDI dans {EdiDirectory})",
            dossierPath, ediDirectory);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(_options.TimeoutMs);

        using var process = new Process { StartInfo = start };
        if (!process.Start())
            throw new InvalidOperationException($"Impossible de démarrer « {exe} ».");

        var stdoutTask = process.StandardOutput.ReadToEndAsync(timeout.Token);
        var stderrTask = process.StandardError.ReadToEndAsync(timeout.Token);

        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            Kill(process);
            throw new InvalidOperationException(
                $"Le pont GénéraFi n'a pas répondu en {_options.TimeoutMs / 1000} s pour le dossier " +
                $"« {dossierPath} ». Augmentez Bridge:TimeoutMs si le dossier est volumineux.");
        }
        catch (OperationCanceledException)
        {
            Kill(process);
            throw;
        }

        var stdout = await stdoutTask;
        var stderr = await stderrTask;

        if (!string.IsNullOrWhiteSpace(stderr))
            _logger.LogWarning("Pont GénéraFi (stderr) : {Stderr}", stderr.Trim());

        if (process.ExitCode != 0)
            throw new InvalidOperationException(ReadError(stdout, process.ExitCode));

        if (string.IsNullOrWhiteSpace(stdout))
            throw new InvalidOperationException(
                "Le pont GénéraFi s'est terminé sans rien renvoyer. Vérifiez qu'il est compilé en x86 " +
                "(le fournisseur Jet 4.0 n'existe qu'en 32 bits).");

        DeclarationPayload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<DeclarationPayload>(stdout, Json);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException(
                $"Réponse illisible du pont GénéraFi : {Preview(stdout)}", ex);
        }

        if (payload is null)
            throw new InvalidOperationException("Réponse vide du pont GénéraFi.");

        _logger.LogInformation(
            "Données chargées : {Societe} ({FiscalId}), période {Mois}/{Annee}, {Total} ligne(s) dont {NonZero} non nulle(s)",
            payload.Societe?.RaisonSociale ?? payload.Societe?.Nom,
            payload.Societe?.FiscalId,
            payload.Periode?.Mois,
            payload.Periode?.Annee,
            payload.Lignes?.Count ?? 0,
            payload.NonZeroLignes.Count());

        return payload;
    }

    private static string ReadError(string stdout, int exitCode)
    {
        if (!string.IsNullOrWhiteSpace(stdout))
        {
            try
            {
                var failure = JsonSerializer.Deserialize<BridgeError>(stdout, Json);
                if (!string.IsNullOrWhiteSpace(failure?.Error))
                    return failure!.Error!;
            }
            catch (JsonException)
            {
            }
        }

        return $"Le pont GénéraFi a échoué (code {exitCode}). {Preview(stdout)}";
    }

    private static string Preview(string text) =>
        string.IsNullOrWhiteSpace(text)
            ? "(aucune sortie)"
            : text.Length <= 300 ? text.Trim() : text[..300].Trim() + "…";

    private static void Kill(Process process)
    {
        try
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
        }
        catch
        {
        }
    }

    private sealed record BridgeError(string? Error, string? Detail);
}
