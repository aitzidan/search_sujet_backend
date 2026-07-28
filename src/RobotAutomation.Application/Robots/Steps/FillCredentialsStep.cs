using Microsoft.Extensions.Logging;
using RobotAutomation.Application.Robots.Abstractions;

namespace RobotAutomation.Application.Robots.Steps;

/// <summary>
/// Fills the username and password fields from the run parameters.
/// Credential values are never logged.
/// </summary>
public sealed class FillCredentialsStep : IRobotStep
{
    public string Name => "Saisie des identifiants";

    public async Task ExecuteAsync(RobotContext ctx, CancellationToken ct)
    {
        var username = LoginCredentials.Username(ctx);
        var password = LoginCredentials.Password(ctx);

        ctx.Logger.LogInformation("Filling credentials for user {User}", Mask(username));
        await ctx.Page.FillAsync(ctx.Portal.Selectors.UsernameInput, username, ct);
        await ctx.Page.FillAsync(ctx.Portal.Selectors.PasswordInput, password, ct);
    }

    /// <summary>Show only the first and last character so a run can be traced without leaking the identifier.</summary>
    private static string Mask(string value) =>
        value.Length <= 2 ? "**" : $"{value[0]}***{value[^1]}";
}

/// <summary>Resolves login credentials: an explicit run <c>Parameters</c> value wins, otherwise the
/// portal's configured default (<c>DgiPortalOptions.Credentials</c>) — so no robot or step ever
/// hardcodes a username/password.</summary>
internal static class LoginCredentials
{
    public static string Username(RobotContext ctx) => ctx.GetParameter("username") ?? ctx.Portal.Credentials.Username ?? "";
    public static string Password(RobotContext ctx) => ctx.GetParameter("password") ?? ctx.Portal.Credentials.Password ?? "";
}
