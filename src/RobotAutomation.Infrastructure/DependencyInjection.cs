using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using RobotAutomation.Application.Captcha;
using RobotAutomation.Application.Configuration;
using RobotAutomation.Application.Files;
using RobotAutomation.Application.Runs;
using RobotAutomation.Application.Sessions;
using RobotAutomation.Infrastructure.Captcha;
using RobotAutomation.Infrastructure.Files;
using RobotAutomation.Infrastructure.Playwright;
using RobotAutomation.Infrastructure.Runs;

namespace RobotAutomation.Infrastructure;

/// <summary>Composition root for Infrastructure (Playwright driver, run store/queue/worker, config).</summary>
public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        // Named portal options — the swap-to-real-DGI seam. Selected per run by name ("fake"/"real").
        services.Configure<DgiPortalOptions>("fake", configuration.GetSection("DgiPortals:fake"));
        services.Configure<DgiPortalOptions>("real", configuration.GetSection("DgiPortals:real"));
        services.Configure<DgiPortalOptions>("rdv", configuration.GetSection("DgiPortals:rdv"));
        services.Configure<PlaywrightOptions>(configuration.GetSection(PlaywrightOptions.SectionName));

        // Playwright engine — a single browser, one isolated context per run.
        services.AddSingleton<IBrowserSessionFactory, PlaywrightBrowserSessionFactory>();

        // Run state, queue, captcha solver, sample files, and the background executor.
        services.AddSingleton<IRunStore, InMemoryRunStore>();
        services.AddSingleton<IRobotRunQueue, ChannelRobotRunQueue>();
        services.AddSingleton<ICaptchaSolver, DomTextCaptchaSolver>();
        services.AddSingleton<ISampleFileProvider, TempSampleFileProvider>();
        services.AddHostedService<RobotRunWorker>();

        return services;
    }
}
