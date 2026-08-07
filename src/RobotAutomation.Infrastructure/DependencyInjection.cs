using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using RobotAutomation.Application.Configuration;
using RobotAutomation.Application.Declarations;
using RobotAutomation.Application.Runs;
using RobotAutomation.Application.Sessions;
using RobotAutomation.Infrastructure.Declarations;
using RobotAutomation.Infrastructure.Playwright;
using RobotAutomation.Infrastructure.Runs;

[assembly: System.Runtime.Versioning.SupportedOSPlatform("windows")]

namespace RobotAutomation.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<DgiPortalOptions>("real", configuration.GetSection("DgiPortals:real"));
        services.Configure<PlaywrightOptions>(configuration.GetSection(PlaywrightOptions.SectionName));
        services.Configure<BridgeOptions>(configuration.GetSection(BridgeOptions.SectionName));

        services.AddSingleton<IBrowserSessionFactory, PlaywrightBrowserSessionFactory>();

        services.AddSingleton<IRunStore, InMemoryRunStore>();
        services.AddSingleton<IRobotRunQueue, ChannelRobotRunQueue>();

        services.AddSingleton<IDeclarationDataSource, BridgeDeclarationDataSource>();

        services.AddHostedService<RobotRunWorker>();

        return services;
    }
}
