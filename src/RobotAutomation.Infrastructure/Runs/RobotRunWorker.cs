using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RobotAutomation.Application.Configuration;
using RobotAutomation.Application.Execution;
using RobotAutomation.Application.Robots;
using RobotAutomation.Application.Robots.Abstractions;
using RobotAutomation.Application.Runs;
using RobotAutomation.Application.Sessions;
using RobotAutomation.Domain.Enums;

namespace RobotAutomation.Infrastructure.Runs;

/// <summary>
/// Long-running host service: dequeues run requests and executes each on its own task and browser
/// context, capped by a concurrency semaphore. This is the modern, genuinely-parallel form of the
/// legacy per-société loop — every run is independent and isolated.
/// </summary>
internal sealed class RobotRunWorker : BackgroundService
{
    private readonly IRobotRunQueue _queue;
    private readonly IBrowserSessionFactory _sessionFactory;
    private readonly IRunStore _store;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptionsMonitor<DgiPortalOptions> _portals;
    private readonly PlaywrightOptions _options;
    private readonly ILogger<RobotRunWorker> _logger;
    private readonly SemaphoreSlim _concurrency;

    public RobotRunWorker(
        IRobotRunQueue queue,
        IBrowserSessionFactory sessionFactory,
        IRunStore store,
        IServiceScopeFactory scopeFactory,
        IOptionsMonitor<DgiPortalOptions> portals,
        IOptions<PlaywrightOptions> options,
        ILogger<RobotRunWorker> logger)
    {
        _queue = queue;
        _sessionFactory = sessionFactory;
        _store = store;
        _scopeFactory = scopeFactory;
        _portals = portals;
        _options = options.Value;
        _logger = logger;
        _concurrency = new SemaphoreSlim(Math.Max(1, _options.MaxConcurrentRuns));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Robot run worker started (max concurrency {Max})", _options.MaxConcurrentRuns);

        while (!stoppingToken.IsCancellationRequested)
        {
            RobotRunRequest request;
            try
            {
                request = await _queue.DequeueAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            await _concurrency.WaitAsync(stoppingToken);
            _ = Task.Run(async () =>
            {
                try { await RunOneAsync(request, stoppingToken); }
                finally { _concurrency.Release(); }
            }, stoppingToken);
        }
    }

    private async Task RunOneAsync(RobotRunRequest request, CancellationToken stoppingToken)
    {
        var run = _store.GetLive(request.RunId);
        if (run is null)
        {
            _logger.LogWarning("Run {RunId} not found in store; skipping", request.RunId);
            return;
        }

        // Run-scoped cancellation = app shutdown OR a manual cancel OR the run timeout.
        using var runCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        runCts.CancelAfter(_options.RunTimeoutMs);
        _store.RegisterCancellation(request.RunId, runCts);

        IRobotPageSession? session = null;
        try
        {
            var portal = _portals.Get(request.PortalName);
            if (string.IsNullOrWhiteSpace(portal.BaseUrl))
            {
                run.Finish(RobotStatus.Failed,
                    $"Portal '{request.PortalName}' is not configured (missing BaseUrl in appsettings 'DgiPortals').");
                return;
            }

            session = await _sessionFactory.CreateSessionAsync(runCts.Token);

            using var scope = _scopeFactory.CreateScope();
            var executor = scope.ServiceProvider.GetRequiredService<IRobotExecutor>();
            var robot = scope.ServiceProvider.GetRequiredService<IRobotCatalog>().GetRequired(request.RobotKey);

            var ctx = new RobotContext
            {
                RunId = request.RunId,
                Page = session.Page,
                Portal = portal,
                Logger = _logger,
                Parameters = request.Parameters,
                DefaultTimeoutMs = _options.DefaultTimeoutMs
            };

            await executor.ExecuteAsync(robot, run, ctx, runCts.Token);
        }
        catch (OperationCanceledException)
        {
            run.Finish(RobotStatus.Cancelled, "Run cancelled or timed out.");
            _logger.LogWarning("Run {RunId} cancelled or timed out", request.RunId);
        }
        catch (Exception ex)
        {
            run.Finish(RobotStatus.Failed, ex.Message);
            _logger.LogError(ex, "Run {RunId} failed to execute", request.RunId);
        }
        finally
        {
            if (session is not null)
            {
                try { await session.DisposeAsync(); }
                catch (Exception ex) { _logger.LogWarning(ex, "Failed to dispose session for run {RunId}", request.RunId); }
            }
            _store.RemoveCancellation(request.RunId);
        }
    }
}
