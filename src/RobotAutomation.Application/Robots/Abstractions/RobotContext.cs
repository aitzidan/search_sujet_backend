using Microsoft.Extensions.Logging;
using RobotAutomation.Application.Configuration;

namespace RobotAutomation.Application.Robots.Abstractions;

public sealed class RobotContext
{
    public required Guid RunId { get; init; }

    public required IRobotPage Page { get; init; }

    public required DgiPortalOptions Portal { get; init; }

    public required ILogger Logger { get; init; }

    /// <summary>Run inputs keyed by name: the client's dossier, the période, the régime.</summary>
    public required IReadOnlyDictionary<string, string?> Parameters { get; init; }

    public required int DefaultTimeoutMs { get; init; }

    /// <summary>
    /// Whether the run drives a browser window a human can actually see and type into. Steps that hand
    /// off to the operator — the login, the CAPTCHA, the one-time code — must refuse to run when this is
    /// false, since there would be nobody able to answer them.
    /// </summary>
    public bool BrowserVisible { get; init; }

    public IDictionary<string, object?> Items { get; } = new Dictionary<string, object?>();

    /// <summary>Data the robot extracts and returns to the caller, exposed on the status response.</summary>
    public IDictionary<string, string?> Output { get; } = new Dictionary<string, string?>();

    public string? GetParameter(string key) =>
        Parameters.TryGetValue(key, out var value) ? value : null;
}
