using Microsoft.Extensions.Logging;
using RobotAutomation.Application.Robots.Abstractions;

namespace RobotAutomation.Application.Robots.Steps;

/// <summary>
/// Hands the login over to the operator: the robot opens the portal in a visible browser and waits
/// while the human types identifier, password and CAPTCHA and submits.
///
/// This is deliberately not automated. The portal's CAPTCHA exists to keep scripts out, and the
/// account's credentials therefore never have to be stored anywhere — the robot only takes over once
/// a human has authenticated, to do the repetitive work that follows.
/// </summary>
public sealed class AwaitManualLoginStep : IRobotStep
{
    private const int PollIntervalMs = 500;

    public string Name => "Connexion par l'utilisateur";

    /// <summary>Never retried — re-running it would restart a wait the operator already satisfied.</summary>
    public bool Retryable => false;

    public async Task ExecuteAsync(RobotContext ctx, CancellationToken ct)
    {
        if (!ctx.BrowserVisible)
            throw new InvalidOperationException(
                "Ce robot exige une fenêtre de navigateur visible : l'utilisateur doit saisir lui-même " +
                "l'identifiant, le mot de passe et le CAPTCHA. Mettez Playwright:Headless à false.");

        var timeout = ctx.Portal.ManualInputTimeoutMs;
        ctx.Logger.LogWarning(
            "EN ATTENTE DE L'UTILISATEUR — saisissez identifiant, mot de passe et CAPTCHA dans la fenêtre " +
            "du navigateur, puis cliquez sur « connexion » ({Seconds} s max)", timeout / 1000);

        if (!await ManualWait.UntilAsync(ctx, timeout, PollIntervalMs, LoggedInAsync, ct))
            throw new InvalidOperationException(
                $"Aucune connexion détectée après {timeout / 1000} s. Le formulaire de connexion est-il " +
                "toujours affiché (identifiant, mot de passe ou CAPTCHA incorrect) ?");

        ctx.Logger.LogInformation("Connexion réussie — le portail a quitté le formulaire de connexion");
    }

    /// <summary>
    /// Accepts either outcome the portal can produce: the one-time-code page appearing, or the login
    /// form simply disappearing (in case an account is ever not challenged for a code).
    /// </summary>
    private static async Task<bool> LoggedInAsync(RobotContext ctx, CancellationToken ct)
    {
        var codePage = ctx.Portal.Selectors.SuccessIndicator;
        if (!string.IsNullOrWhiteSpace(codePage) && await ctx.Page.IsVisibleAsync(codePage, ct))
            return true;

        var loginForm = ctx.Portal.SuccessRule.HiddenSelector;
        return !string.IsNullOrWhiteSpace(loginForm) && !await ctx.Page.IsVisibleAsync(loginForm, ct);
    }
}

/// <summary>
/// Waits for the operator to type the 6-digit code the portal e-mails after a successful login, and
/// to validate it. The code only exists in the user's mailbox, so this cannot be automated — the robot
/// just watches for the code page to give way.
///
/// Skips itself when no code page is present, so an account that is not challenged still flows through.
/// </summary>
public sealed class AwaitOneTimeCodeStep : IRobotStep
{
    private const int PollIntervalMs = 500;

    public string Name => "Code de vérification par l'utilisateur";

    public bool Retryable => false;

    public async Task ExecuteAsync(RobotContext ctx, CancellationToken ct)
    {
        var codePage = ctx.Portal.Selectors.SuccessIndicator;
        if (string.IsNullOrWhiteSpace(codePage) || !await ctx.Page.IsVisibleAsync(codePage, ct))
        {
            ctx.Logger.LogInformation("Aucune page de code de vérification — étape ignorée");
            return;
        }

        if (!ctx.BrowserVisible)
            throw new InvalidOperationException(
                "Ce robot exige une fenêtre de navigateur visible : l'utilisateur doit saisir le code " +
                "de vérification reçu par e-mail. Mettez Playwright:Headless à false.");

        var timeout = ctx.Portal.ManualInputTimeoutMs;
        ctx.Logger.LogWarning(
            "EN ATTENTE DE L'UTILISATEUR — saisissez le code à 6 chiffres reçu par e-mail dans la fenêtre " +
            "du navigateur, puis cliquez sur « valider » ({Seconds} s max)", timeout / 1000);

        // Accepted <=> the code page is replaced by the authenticated application.
        var accepted = await ManualWait.UntilAsync(
            ctx, timeout, PollIntervalMs,
            async (c, token) => !await c.Page.IsVisibleAsync(codePage, token), ct);

        if (!accepted)
            throw new InvalidOperationException(
                $"Le code de vérification n'a pas été validé après {timeout / 1000} s.");

        ctx.Logger.LogInformation("Code de vérification accepté — session authentifiée, le robot prend le relais");
    }
}

/// <summary>Polls a condition until it holds or the (human-scale) deadline passes.</summary>
internal static class ManualWait
{
    public static async Task<bool> UntilAsync(
        RobotContext ctx,
        int timeoutMs,
        int pollIntervalMs,
        Func<RobotContext, CancellationToken, Task<bool>> condition,
        CancellationToken ct)
    {
        var deadline = DateTimeOffset.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (await condition(ctx, ct)) return true;
            await Task.Delay(pollIntervalMs, ct);
        }
        return false;
    }
}
