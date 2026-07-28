using RobotAutomation.Application.Robots.Abstractions;

namespace RobotAutomation.Application.Robots.Steps;

/// <summary>
/// Polls a condition until it holds or a deadline passes.
///
/// Steps should prefer a locator wait (<c>WaitForSelectorAsync</c>/<c>WaitForHiddenAsync</c>) whenever the
/// condition is "this element appears/disappears" — those auto-wait inside the browser. This helper exists
/// for the conditions a single locator cannot express: a row <em>count</em> dropping after a delete, a
/// dialog being replaced by a different dialog, or a wait measured on human time (an operator typing a
/// CAPTCHA or a one-time code).
/// </summary>
internal static class Poll
{
    public static async Task<bool> UntilAsync(
        RobotContext ctx,
        int timeoutMs,
        int pollIntervalMs,
        Func<RobotContext, CancellationToken, Task<bool>> condition,
        CancellationToken ct)
    {
        // do/while: the condition is evaluated at least once, so a zero timeout still means "check now"
        // rather than "skip the check".
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
