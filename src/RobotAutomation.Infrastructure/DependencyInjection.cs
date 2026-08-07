using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using RobotAutomation.Application.Configuration;
using RobotAutomation.Application.Declarations;
using RobotAutomation.Application.Runs;
using RobotAutomation.Application.Sessions;
using RobotAutomation.Infrastructure.Declarations;
using RobotAutomation.Infrastructure.Playwright;
using RobotAutomation.Infrastructure.Runs;

// The whole solution runs Windows-only: the robot drives a headed Chromium, and the declaration data
// comes from a 32-bit Windows bridge executable (see BridgeDeclarationDataSource).
[assembly: System.Runtime.Versioning.SupportedOSPlatform("windows")]

namespace RobotAutomation.Infrastructure;

/// <summary>Composition root for Infrastructure (Playwright driver, run store/queue/worker, config).</summary>
public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        // Named portal options, selected per run by name. "real" is the live TVA portal
        // (tva.tax.gov.ma); adding a section here is all a further portal needs.
        services.Configure<DgiPortalOptions>("real", configuration.GetSection("DgiPortals:real"));
        services.Configure<PlaywrightOptions>(configuration.GetSection(PlaywrightOptions.SectionName));
        services.Configure<BridgeOptions>(configuration.GetSection(BridgeOptions.SectionName));

        // Playwright engine — a single browser, one isolated context per run.
        services.AddSingleton<IBrowserSessionFactory, PlaywrightBrowserSessionFactory>();

        // Run state, queue, and the background executor.
        services.AddSingleton<IRunStore, InMemoryRunStore>();
        services.AddSingleton<IRobotRunQueue, ChannelRobotRunQueue>();

        // Legacy data bridge — a 32-bit child process per call, so this adapter stays stateless and a
        // singleton is safe (see BridgeDeclarationDataSource for why the process boundary exists).
        services.AddSingleton<IDeclarationDataSource, BridgeDeclarationDataSource>();

        services.AddHostedService<RobotRunWorker>();

        return services;
    }
}
