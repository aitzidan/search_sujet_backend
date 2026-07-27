using RobotAutomation.Application.Robots;

namespace RobotAutomation.Application.Sessions;

/// <summary>
/// Creates an isolated browser session per run. Implemented in Infrastructure by a singleton
/// that owns one Playwright <c>IBrowser</c> and hands out a fresh <c>IBrowserContext</c> (its own
/// cookies/storage) for each run — so concurrent runs cannot see or interfere with one another.
/// </summary>
public interface IBrowserSessionFactory
{
    Task<IRobotPageSession> CreateSessionAsync(CancellationToken ct);
}

/// <summary>
/// A single run's browser context + page. Disposing it tears the context down (called in a
/// <c>finally</c> so a browser context is never leaked, even on failure or cancellation).
/// </summary>
public interface IRobotPageSession : IAsyncDisposable
{
    IRobotPage Page { get; }
}
