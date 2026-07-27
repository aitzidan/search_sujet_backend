using FluentValidation;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using RobotAutomation.Domain.Common;

namespace RobotAutomation.WebApi.Middleware;

/// <summary>
/// Translates control-plane exceptions into RFC 7807 ProblemDetails responses:
/// unknown robot/run -> 404, validation failure -> 400, anything else -> 500 (logged).
/// Run failures are NOT exceptions here — they surface as RobotStatus.Failed on GET.
/// </summary>
internal sealed class GlobalExceptionHandler : IExceptionHandler
{
    private readonly ILogger<GlobalExceptionHandler> _logger;

    public GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger) => _logger = logger;

    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken ct)
    {
        var problem = new ProblemDetails();

        switch (exception)
        {
            case NotFoundException notFound:
                problem.Status = StatusCodes.Status404NotFound;
                problem.Title = "Resource not found";
                problem.Detail = notFound.Message;
                break;

            case ValidationException validation:
                problem.Status = StatusCodes.Status400BadRequest;
                problem.Title = "Validation failed";
                problem.Detail = "One or more validation errors occurred.";
                problem.Extensions["errors"] = validation.Errors
                    .Select(e => new { field = e.PropertyName, error = e.ErrorMessage });
                break;

            default:
                problem.Status = StatusCodes.Status500InternalServerError;
                problem.Title = "An unexpected error occurred.";
                _logger.LogError(exception, "Unhandled exception");
                break;
        }

        httpContext.Response.StatusCode = problem.Status.Value;
        await httpContext.Response.WriteAsJsonAsync(problem, ct);
        return true;
    }
}
