using RobotAutomation.Application.Robots;

namespace RobotAutomation.Application.Sessions;

/// <summary>
/// Creates an isolated browser session per run. Implemented in Infrastructure by a singleton
/// that owns one Playwright <c>IBrowser</c> and hands out a fresh <c>IBrowserContext</c> (its own
/// cookies/storage) for each run — so concurrent runs cannot see or interfere with one another.
/// </summary>
public interface IBrowserSessionFactory
{
    Task<IRobotPageSession> CreateSessionAsync(RobotSessionOptions options, CancellationToken ct);
}

/// <summary>Per-run session settings.</summary>
/// <param name="StorageStatePath">
/// When set, cookies and localStorage are restored from this file at session start (if it exists) and
/// written back by <see cref="IRobotPageSession.SaveStateAsync"/>. This is what lets one run reuse a
/// login another run already performed. Null keeps the session fully isolated — the default, and what
/// every robot except the interactive real-portal ones should use.
/// </param>
public sealed record RobotSessionOptions(string? StorageStatePath = null);

/// <summary>
/// A single run's browser context + page. Disposing it tears the context down (called in a
/// <c>finally</c> so a browser context is never leaked, even on failure or cancellation).
/// </summary>
public interface IRobotPageSession : IAsyncDisposable
{
    IRobotPage Page { get; }

    /// <summary>
    /// Persists cookies/localStorage to the configured storage-state file so a later run can reuse the
    /// authentication. No-op when no path was configured. Must be called before disposal, since
    /// disposing closes the context the state is read from.
    /// </summary>
    Task SaveStateAsync(CancellationToken ct);
}
