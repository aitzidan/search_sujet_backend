using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Retry;
using RobotAutomation.Application.Configuration;
using RobotAutomation.Application.Robots.Abstractions;
using RobotAutomation.Application.Runs;
using RobotAutomation.Domain.Enums;

namespace RobotAutomation.Application.Execution;

/// <summary>
/// Iterates a robot's steps in order (the modern, explicit replacement for the legacy URL-driven
/// <c>switch(etape)</c> dispatch). Per step it: records a Running entry, runs the step (wrapped in a
/// Polly retry pipeline when the step is retryable), then records Succeeded — or, on failure,
/// captures a screenshot, records Failed with the error, and stops the sequence (the modern
/// <c>AbandonnerDeclaration</c>). Cancellation ends the run as Cancelled.
/// </summary>
public sealed class RobotExecutor : IRobotExecutor
{
    private readonly PlaywrightOptions _options;
    private readonly ILogger<RobotExecutor> _logger;
    private readonly ResiliencePipeline _retryPipeline;

    public RobotExecutor(IOptions<PlaywrightOptions> options, ILogger<RobotExecutor> logger)
    {
        _options = options.Value;
        _logger = logger;
        _retryPipeline = BuildRetryPipeline(_options.MaxStepRetries);
    }

    public async Task ExecuteAsync(IRobot robot, RobotRun run, RobotContext ctx, CancellationToken ct)
    {
        using var _ = _logger.BeginScope(new Dictionary<string, object>
        {
            ["RunId"] = run.RunId,
            ["RobotKey"] = robot.Key,
            ["Portal"] = run.PortalName
        });

        run.MarkRunning();
        _logger.LogInformation("Run started with {StepCount} steps", robot.Steps.Count);

        foreach (var step in robot.Steps)
        {
            if (ct.IsCancellationRequested)
            {
                run.Finish(RobotStatus.Cancelled, "Run cancelled before completion.");
                _logger.LogWarning("Run cancelled");
                return;
            }

            var order = run.BeginStep(step.Name);
            var startedAt = DateTimeOffset.UtcNow;

            try
            {
                if (step.Retryable && _options.MaxStepRetries > 0)
                    await _retryPipeline.ExecuteAsync(async token => await step.ExecuteAsync(ctx, token), ct);
                else
                    await step.ExecuteAsync(ctx, ct);

                run.CompleteStep(order, StepStatus.Succeeded, null, null, ctx.Page.Url);
                _logger.LogInformation("Step {Order} '{Step}' succeeded in {Ms} ms",
                    order, step.Name, (DateTimeOffset.UtcNow - startedAt).TotalMilliseconds);
            }
            catch (OperationCanceledException)
            {
                run.CompleteStep(order, StepStatus.Skipped, "Cancelled", null, ctx.Page.Url);
                run.Finish(RobotStatus.Cancelled, "Run cancelled during execution.");
                _logger.LogWarning("Step {Order} '{Step}' cancelled", order, step.Name);
                return;
            }
            catch (Exception ex)
            {
                var screenshot = await TryScreenshotAsync(ctx, run.RunId, order, step.Name);
                run.CompleteStep(order, StepStatus.Failed, ex.Message, screenshot, ctx.Page.Url);
                run.SetData(ctx.Output);
                run.Finish(RobotStatus.Failed, $"Step '{step.Name}' failed: {ex.Message}");
                _logger.LogError(ex, "Step {Order} '{Step}' failed", order, step.Name);
                return;
            }
        }

        // Optional success artifact — a final screenshot for the audit trail / demo.
        var successShot = await TryScreenshotAsync(ctx, run.RunId, robot.Steps.Count, "success");
        if (successShot is not null) run.AddScreenshot(successShot);
        run.SetData(ctx.Output);
        run.Finish(RobotStatus.Succeeded);
        _logger.LogInformation("Run succeeded");
    }

    private async Task<string?> TryScreenshotAsync(RobotContext ctx, Guid runId, int order, string stepName)
    {
        try
        {
            var baseDir = _options.ScreenshotDirectory
                          ?? Path.Combine(Path.GetTempPath(), "robot-automation", "screenshots");
            var slug = new string(stepName.Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray());
            var path = Path.Combine(baseDir, runId.ToString("N"), $"{order:D2}-{slug}.png");
            return await ctx.Page.ScreenshotAsync(path, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to capture screenshot for step {Order}", order);
            return null;
        }
    }

    private static ResiliencePipeline BuildRetryPipeline(int maxRetries)
    {
        if (maxRetries <= 0) return ResiliencePipeline.Empty;

        return new ResiliencePipelineBuilder()
            .AddRetry(new RetryStrategyOptions
            {
                MaxRetryAttempts = maxRetries,
                Delay = TimeSpan.FromMilliseconds(500),
                BackoffType = DelayBackoffType.Constant,
                ShouldHandle = new PredicateBuilder().Handle<Exception>(e => e is not OperationCanceledException)
            })
            .Build();
    }
}
