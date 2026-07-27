using Microsoft.Extensions.Logging;
using RobotAutomation.Application.Robots.Abstractions;

namespace RobotAutomation.Application.Robots.Steps;

/// <summary>
/// Clicks the login submit button. Marked non-retryable so Polly never double-submits —
/// the pattern that must carry over to the real DGI's "submit for validation".
/// </summary>
public sealed class SubmitLoginStep : IRobotStep
{
    public string Name => "Soumission du formulaire";

    public bool Retryable => false;

    public async Task ExecuteAsync(RobotContext ctx, CancellationToken ct)
    {
        ctx.Logger.LogInformation("Submitting login form");
        await ctx.Page.ClickAsync(ctx.Portal.Selectors.SubmitButton, ct);
    }
}
