using RobotAutomation.Application.Robots.Abstractions;

namespace RobotAutomation.Application.Robots.Steps;

internal static class Poll
{
    public static async Task<bool> UntilAsync(
        RobotContext ctx,
        int timeoutMs,
        int pollIntervalMs,
        Func<RobotContext, CancellationToken, Task<bool>> condition,
        CancellationToken ct)
    {
        var deadline = DateTimeOffset.UtcNow.AddMilliseconds(timeoutMs);
        do
        {
            if (await condition(ctx, ct)) return true;
            await Task.Delay(pollIntervalMs, ct);
        }
        while (DateTimeOffset.UtcNow < deadline);

        return false;
    }
}
