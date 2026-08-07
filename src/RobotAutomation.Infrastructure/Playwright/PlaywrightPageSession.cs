using Microsoft.Playwright;
using RobotAutomation.Application.Robots;
using RobotAutomation.Application.Sessions;

namespace RobotAutomation.Infrastructure.Playwright;

internal sealed class PlaywrightPageSession : IRobotPageSession
{
    private readonly IBrowserContext _context;
    private readonly string? _storageStatePath;

    public PlaywrightPageSession(IBrowserContext context, IRobotPage page, string? storageStatePath = null)
    {
        _context = context;
        Page = page;
        _storageStatePath = storageStatePath;
    }

    public IRobotPage Page { get; }

    public async Task SaveStateAsync(CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_storageStatePath)) return;

        var dir = Path.GetDirectoryName(_storageStatePath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        await _context.StorageStateAsync(new BrowserContextStorageStateOptions { Path = _storageStatePath });
    }

    public async ValueTask DisposeAsync() => await _context.DisposeAsync();
}
