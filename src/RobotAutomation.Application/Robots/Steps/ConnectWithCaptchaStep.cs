using Microsoft.Extensions.Logging;
using RobotAutomation.Application.Captcha;
using RobotAutomation.Application.Robots.Abstractions;

namespace RobotAutomation.Application.Robots.Steps;

/// <summary>
/// Solves the CAPTCHA, submits the login form, and checks whether the portal actually accepted it —
/// retrying with a freshly loaded CAPTCHA when it did not.
///
/// These three actions are one step rather than three because the retry couples them: OCR misreads a
/// distorted CAPTCHA some of the time, and the only way to know is to submit and look at the result.
/// A rejected attempt reloads the login page, which serves a new CAPTCHA image to try.
///
/// Success is "the page moved on" — on the real TVA portal the login form is replaced by the
/// e-mailed 6-digit code form (<c>app-codeacces</c>), configured as <c>Selectors.SuccessIndicator</c>.
/// </summary>
public sealed class ConnectWithCaptchaStep : IRobotStep
{
    /// <summary>How long to wait for the portal to either advance or re-render the login form after submit.</summary>
    private const int OutcomeTimeoutMs = 12_000;
    private const int PollIntervalMs = 400;

    private readonly ICaptchaSolver _captchaSolver;

    public ConnectWithCaptchaStep(ICaptchaSolver captchaSolver) => _captchaSolver = captchaSolver;

    public string Name => "Connexion (CAPTCHA + soumission)";

    /// <summary>Owns its own bounded retry loop; Polly must never re-run it and double-submit.</summary>
    public bool Retryable => false;

    public async Task ExecuteAsync(RobotContext ctx, CancellationToken ct)
    {
        var maxAttempts = Math.Max(1, ctx.Portal.CaptchaMaxAttempts);
        var failures = new List<string>();

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            // A rejected attempt leaves a stale form and a spent CAPTCHA — reload for a fresh one.
            if (attempt > 1)
            {
                ctx.Logger.LogInformation("Reloading the login page for a fresh CAPTCHA (attempt {Attempt}/{Max})", attempt, maxAttempts);
                await ctx.Page.GotoAsync(ctx.Portal.FullUrl, ctx.Portal.NavigationWaitUntil, ct);
                await ctx.Page.WaitForSelectorAsync(ctx.Portal.Selectors.UsernameInput, ctx.DefaultTimeoutMs, ct);
                await ctx.Page.FillAsync(ctx.Portal.Selectors.UsernameInput, LoginCredentials.Username(ctx), ct);
                await ctx.Page.FillAsync(ctx.Portal.Selectors.PasswordInput, LoginCredentials.Password(ctx), ct);
            }

            ctx.Items["captchaAttempt"] = attempt; // lets the solver file its diagnostics per attempt
            var code = await _captchaSolver.SolveAsync(ctx, ct);
            ctx.Items["captcha"] = code;
            if (!string.IsNullOrEmpty(code))
                await ctx.Page.FillAsync(ctx.Portal.Selectors.CaptchaInput, code, ct);

            await EnsureRequiredFieldsAsync(ctx, code, ct);

            ctx.Logger.LogInformation(
                "Submitting login — attempt {Attempt}/{Max}, CAPTCHA saisi « {Code} »", attempt, maxAttempts, code);
            await ctx.Page.ClickAsync(ctx.Portal.Selectors.SubmitButton, ct);

            if (await AdvancedPastLoginAsync(ctx, ct))
            {
                ctx.Output["captchaAttempts"] = attempt.ToString();
                ctx.Logger.LogInformation("Portail : connexion acceptée à la tentative {Attempt}", attempt);
                return;
            }

            var reason = await ReadErrorAsync(ctx, ct);
            failures.Add($"tentative {attempt} (CAPTCHA « {code} ») : {reason ?? "refusée sans message"}");
            ctx.Logger.LogWarning(
                "Portail : connexion refusée (tentative {Attempt}/{Max}), CAPTCHA lu « {Code} » — {Reason}",
                attempt, maxAttempts, code, reason ?? "aucun message d'erreur détecté");
        }

        throw new InvalidOperationException(
            $"Le portail a refusé la connexion après {maxAttempts} tentative(s) : {string.Join(" | ", failures)}. " +
            "Cause la plus probable : le CAPTCHA a été mal lu par l'OCR (comparez avec les images dans le dossier de " +
            "diagnostic du run). Vérifiez aussi l'identifiant et le mot de passe, et basculez " +
            "DgiPortals:real:CaptchaMode sur \"Manual\" pour saisir le code vous-même.");
    }

    /// <summary>
    /// Whether the portal moved off the login form. Prefers a positive signal — the configured
    /// <c>SuccessIndicator</c> appearing — and only falls back to "the login form vanished" when no
    /// indicator is configured, since a form can also be briefly absent mid-render.
    /// </summary>
    private static async Task<bool> AdvancedPastLoginAsync(RobotContext ctx, CancellationToken ct)
    {
        var indicator = ctx.Portal.Selectors.SuccessIndicator;
        var loginForm = ctx.Portal.SuccessRule.HiddenSelector;
        var deadline = DateTimeOffset.UtcNow.AddMilliseconds(OutcomeTimeoutMs);

        while (DateTimeOffset.UtcNow < deadline)
        {
            if (!string.IsNullOrWhiteSpace(indicator))
            {
                if (await ctx.Page.IsVisibleAsync(indicator, ct)) return true;
            }
            else if (!string.IsNullOrWhiteSpace(loginForm) && !await ctx.Page.IsVisibleAsync(loginForm, ct))
            {
                return true;
            }

            await Task.Delay(PollIntervalMs, ct);
        }

        return false;
    }

    /// <summary>Best-effort error text from the portal (a rejected CAPTCHA/credential banner), for the log.</summary>
    private static async Task<string?> ReadErrorAsync(RobotContext ctx, CancellationToken ct)
    {
        var selector = ctx.Portal.SuccessRule.ContentSelector;
        if (string.IsNullOrWhiteSpace(selector)) return null;

        try
        {
            if (!await ctx.Page.IsVisibleAsync(selector, ct)) return null;
            var text = await ctx.Page.GetTextAsync(selector, ct);
            return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
        }
        catch
        {
            return null; // never let error-reporting break the retry loop
        }
    }

    /// <summary>Re-checks every required field is populated immediately before submitting, refilling any
    /// the SPA cleared, and refuses to submit a form that is missing the CAPTCHA.</summary>
    private static async Task EnsureRequiredFieldsAsync(RobotContext ctx, string captcha, CancellationToken ct)
    {
        await EnsureFilledAsync(ctx, ctx.Portal.Selectors.UsernameInput, LoginCredentials.Username(ctx), ct);
        await EnsureFilledAsync(ctx, ctx.Portal.Selectors.PasswordInput, LoginCredentials.Password(ctx), ct);
        await EnsureFilledAsync(ctx, ctx.Portal.Selectors.CaptchaInput, captcha, ct);

        var captchaSelector = ctx.Portal.Selectors.CaptchaInput;
        if (string.IsNullOrWhiteSpace(captchaSelector)) return;

        if (string.IsNullOrWhiteSpace(await ctx.Page.GetValueAsync(captchaSelector, ct)))
            throw new InvalidOperationException(
                "Le champ CAPTCHA est vide — soumission annulée pour ne pas consommer une tentative de connexion.");
    }

    private static async Task EnsureFilledAsync(RobotContext ctx, string selector, string expected, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(selector) || string.IsNullOrEmpty(expected)) return;
        if (string.IsNullOrEmpty(await ctx.Page.GetValueAsync(selector, ct)))
            await ctx.Page.FillAsync(selector, expected, ct);
    }
}
