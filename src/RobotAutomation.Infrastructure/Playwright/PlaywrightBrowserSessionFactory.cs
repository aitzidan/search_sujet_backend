using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Playwright;
using RobotAutomation.Application.Configuration;
using RobotAutomation.Application.Sessions;

namespace RobotAutomation.Infrastructure.Playwright;

/// <summary>
/// Singleton that owns one Playwright instance and one <see cref="IBrowser"/> (both expensive to
/// create), lazily launched behind a lock. Each run gets a fresh <see cref="IBrowserContext"/> —
/// an isolated incognito profile — so concurrent runs cannot see each other's cookies/state.
/// Disposed by the DI container on shutdown, which closes the browser.
/// </summary>
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

    public async Task<IRobotPageSession> CreateSessionAsync(CancellationToken ct)
    {
        var browser = await GetBrowserAsync(ct);
        var context = await browser.NewContextAsync(new BrowserNewContextOptions
        {
            IgnoreHTTPSErrors = true
        });
        context.SetDefaultTimeout(_options.DefaultTimeoutMs);
        var page = await context.NewPageAsync();
        return new PlaywrightPageSession(context, new PlaywrightRobotPage(page));
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
