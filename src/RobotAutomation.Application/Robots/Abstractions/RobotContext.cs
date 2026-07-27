using Microsoft.Extensions.Logging;
using RobotAutomation.Application.Configuration;

namespace RobotAutomation.Application.Robots.Abstractions;

/// <summary>
/// Everything a step needs at run time, and a scratchpad for passing state between steps.
/// Created fresh per run by the worker; never shared across runs.
/// </summary>
public sealed class RobotContext
{
    public required Guid RunId { get; init; }

    /// <summary>The browser page this run drives (backed by an isolated Playwright context).</summary>
    public required IRobotPage Page { get; init; }

    /// <summary>Portal config selected for this run (selectors, base URL, success rule).</summary>
    public required DgiPortalOptions Portal { get; init; }

    public required ILogger Logger { get; init; }

    /// <summary>
    /// Run inputs such as credentials, keyed by name (e.g. "username", "password").
    /// Sourced from the request; kept only in memory for the life of the run and never persisted.
    /// </summary>
    public required IReadOnlyDictionary<string, string?> Parameters { get; init; }

    /// <summary>Default wait/action timeout (ms) for steps, taken from PlaywrightOptions at run start.</summary>
    public required int DefaultTimeoutMs { get; init; }

    /// <summary>Inter-step scratch state, e.g. the solved CAPTCHA code produced by one step and used by the next.</summary>
    public IDictionary<string, object?> Items { get; } = new Dictionary<string, object?>();

    /// <summary>
    /// Data the robot extracts and returns to the caller (e.g. an appointment confirmation number,
    /// date, time). Copied into the run and exposed on the status response.
    /// </summary>
    public IDictionary<string, string?> Output { get; } = new Dictionary<string, string?>();

    public string? GetParameter(string key) =>
        Parameters.TryGetValue(key, out var value) ? value : null;
}
