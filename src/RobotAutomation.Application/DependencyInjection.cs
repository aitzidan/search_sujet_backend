using FluentValidation;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using RobotAutomation.Application.Common.Behaviors;
using RobotAutomation.Application.Execution;
using RobotAutomation.Application.Robots;
using RobotAutomation.Application.Robots.Abstractions;
using RobotAutomation.Application.Robots.Steps;

namespace RobotAutomation.Application;

/// <summary>Composition root for the Application layer (the robot framework + control-plane).</summary>
public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        var assembly = typeof(DependencyInjection).Assembly;

        services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(assembly));
        services.AddValidatorsFromAssembly(assembly);
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));

        // Reusable steps (stateless singletons — they share nothing between runs except via RobotContext).
        // Operator hand-off for the login, then the declaration flow.
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

        // Robots. Add more IRobot registrations here and they appear in the API automatically.
        services.AddSingleton<IRobot, DgiTvaRobot>();            // real TVA portal: login + declaration
        services.AddSingleton<IRobotCatalog, RobotCatalog>();

        // The sequencing engine (one instance per run scope).
        services.AddScoped<IRobotExecutor, RobotExecutor>();

        return services;
    }
}
