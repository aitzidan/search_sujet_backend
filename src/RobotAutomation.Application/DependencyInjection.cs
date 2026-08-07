using FluentValidation;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using RobotAutomation.Application.Common.Behaviors;
using RobotAutomation.Application.Execution;
using RobotAutomation.Application.Robots;
using RobotAutomation.Application.Robots.Abstractions;
using RobotAutomation.Application.Robots.Steps;

namespace RobotAutomation.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        var assembly = typeof(DependencyInjection).Assembly;

        services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(assembly));
        services.AddValidatorsFromAssembly(assembly);
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));

        services.AddSingleton<LoadDeclarationDataStep>();
        services.AddSingleton<OpenPortalStep>();
        services.AddSingleton<AwaitManualLoginStep>();
        services.AddSingleton<AwaitOneTimeCodeStep>();
        services.AddSingleton<DeleteExistingDeclarationStep>();
        services.AddSingleton<OpenCurrentPeriodDeclarationStep>();
        services.AddSingleton<CreateDeclarationStep>();
        services.AddSingleton<SaveDeclarationStep>();
        services.AddSingleton<OpenEdiUploadStep>();
        services.AddSingleton<SendEdiFileStep>();
        services.AddSingleton<ReturnToDeclarationListStep>();
        services.AddSingleton<EditDeclarationStep>();
        services.AddSingleton<FillDeclarationAmountsStep>();
        services.AddSingleton<RecalculateDeclarationStep>();

        services.AddSingleton<IRobot, DgiTvaRobot>();
        services.AddSingleton<IRobotCatalog, RobotCatalog>();

        services.AddScoped<IRobotExecutor, RobotExecutor>();

        return services;
    }
}
