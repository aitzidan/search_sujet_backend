using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Playwright;
using RobotAutomation.Application.Configuration;
using RobotAutomation.Application.Sessions;

namespace RobotAutomation.Infrastructure.Playwright;

internal sealed class PlaywrightBrowserSessionFactory : IBrowserSessionFactory, IAsyncDisposable
{
    private readonly PlaywrightOptions _options;
    private readonly ILogger<PlaywrightBrowserSessionFactory> _logger;
    private readonly SemaphoreSlim _initLock = new(1, 1);

    private IPlaywright? _playwright;
    private IBrowser? _browser;

    public PlaywrightBrowserSessionFactory(
        IOptions<PlaywrightOptions> options,
        ILogger<PlaywrightBrowserSessionFactory> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public async Task<IRobotPageSession> CreateSessionAsync(RobotSessionOptions options, CancellationToken ct)
    {
        var browser = await GetBrowserAsync(ct);

        var reuse = !string.IsNullOrWhiteSpace(options.StorageStatePath);
        var restore = reuse && File.Exists(options.StorageStatePath);
        if (reuse)
        {
            _logger.LogInformation(
                restore
                    ? "Reusing saved browser session from {Path} — the portal may already be authenticated"
                    : "No saved session at {Path} yet; starting clean and saving it after a successful run",
                options.StorageStatePath);
        }

        var context = await browser.NewContextAsync(new BrowserNewContextOptions
        {
            IgnoreHTTPSErrors = true,
            StorageStatePath = restore ? options.StorageStatePath : null
        });
        context.SetDefaultTimeout(_options.DefaultTimeoutMs);
        var page = await context.NewPageAsync();
        return new PlaywrightPageSession(context, new PlaywrightRobotPage(page), options.StorageStatePath);
    }

    private async Task<IBrowser> GetBrowserAsync(CancellationToken ct)
    {
        if (_browser is not null) return _browser;

        await _initLock.WaitAsync(ct);
        try
        {
            if (_browser is not null) return _browser;

            _logger.LogInformation("Launching Chromium (headless={Headless})", _options.Headless);
            _playwright = await Microsoft.Playwright.Playwright.CreateAsync();
            _browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
            {
                Headless = _options.Headless,
                SlowMo = _options.SlowMoMs
            });
            return _browser;
        }
        catch (PlaywrightException ex) when (ex.Message.Contains("Executable doesn't exist", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Playwright's Chromium browser is not installed. Run: " +
                "pwsh src/RobotAutomation.WebApi/bin/Debug/net9.0/playwright.ps1 install chromium", ex);
        }
        finally
        {
            _initLock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_browser is not null) await _browser.DisposeAsync();
        _playwright?.Dispose();
        _initLock.Dispose();
    }
}
