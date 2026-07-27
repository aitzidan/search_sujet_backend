using Microsoft.Playwright;
using RobotAutomation.Application.Robots;
using RobotAutomation.Application.Sessions;

namespace RobotAutomation.Infrastructure.Playwright;

/// <summary>
/// One run's isolated browser context + page. Disposing closes the context (and its pages),
/// which the worker always does in a <c>finally</c> so a context is never leaked.
/// </summary>
internal sealed class PlaywrightPageSession : IRobotPageSession
{
    private readonly IBrowserContext _context;

    public PlaywrightPageSession(IBrowserContext context, IRobotPage page)
    {
        _context = context;
        Page = page;
    }

    public IRobotPage Page { get; }

    public async ValueTask DisposeAsync() => await _context.DisposeAsync();
}
