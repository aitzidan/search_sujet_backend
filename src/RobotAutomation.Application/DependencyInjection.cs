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
        // Login steps (shared by every robot).
        services.AddSingleton<NavigateStep>();
        services.AddSingleton<FillCredentialsStep>();
        services.AddSingleton<SolveCaptchaStep>();
        services.AddSingleton<SubmitLoginStep>();
        services.AddSingleton<VerifySuccessStep>();
        services.AddSingleton<ConnectWithCaptchaStep>(); // automated CAPTCHA + submit + verify (with retry)
        // Robot 4 (real TVA portal) — operator hand-off for the login, then the declaration flow.
        services.AddSingleton<OpenPortalStep>();
        services.AddSingleton<AwaitManualLoginStep>();
        services.AddSingleton<AwaitOneTimeCodeStep>();
        services.AddSingleton<DeleteExistingDeclarationStep>();
        // Declaration steps (Robot 1).
        services.AddSingleton<OpenDeclarationStep>();
        services.AddSingleton<CreatePeriodStep>();
        services.AddSingleton<UploadEdiFileStep>();
        services.AddSingleton<FillDeclarationStep>();
        services.AddSingleton<SubmitDeclarationStep>();
        services.AddSingleton<VerifyDeclarationStep>();
        // Imported-products steps (Robot 2).
        services.AddSingleton<OpenImportedProductsStep>();
        services.AddSingleton<EnterImportedProductsStep>();
        services.AddSingleton<SubmitImportedProductsStep>();
        services.AddSingleton<VerifyImportedProductsStep>();
        // Rendez-vous steps (Robot 3 — real site).
        services.AddSingleton<OpenRendezVousStep>();
        services.AddSingleton<SelectPrestationStep>();
        services.AddSingleton<ChooseSlotStep>();
        services.AddSingleton<FillValidationStep>();
        services.AddSingleton<ConfirmRendezVousStep>();
        services.AddSingleton<CaptureConfirmationStep>();

        // Robots. Add more IRobot registrations here and they appear in the API automatically.
        services.AddSingleton<IRobot, DgiLoginRobot>();          // simple login (kept for Swagger/self-test)
        services.AddSingleton<IRobot, DgiDeclarationRobot>();    // Robot 1 — full télédéclaration flow
        services.AddSingleton<IRobot, DgiImportedProductsRobot>(); // Robot 2 — beyond login: imported products
        services.AddSingleton<IRobot, DgiRendezVousRobot>();     // Robot 3 — real rendez-vous portal
        services.AddSingleton<IRobot, DgiTvaRobot>();            // Robot 4 — real TVA portal: login + declaration
        services.AddSingleton<IRobotCatalog, RobotCatalog>();

        // The sequencing engine (one instance per run scope).
        services.AddScoped<IRobotExecutor, RobotExecutor>();

        return services;
    }
}
