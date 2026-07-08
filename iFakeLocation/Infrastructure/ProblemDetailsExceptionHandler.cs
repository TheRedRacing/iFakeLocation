using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using iFakeLocation.Services;
using iFakeLocation.Services.PyMobileDevice;
using iFakeLocation.Services.RouteSimulation;

namespace iFakeLocation.Infrastructure;

/// <summary>
/// Maps the small catalog of expected domain exceptions to RFC 7807 ProblemDetails responses,
/// replacing the original app's ad-hoc `{"error": "..."}` JSON blobs. Anything not recognized
/// here is logged and surfaced as a generic 500.
/// </summary>
internal sealed class ProblemDetailsExceptionHandler(ILogger<ProblemDetailsExceptionHandler> logger) : IExceptionHandler {
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken) {
        var (statusCode, title) = exception switch {
            DeviceNotFoundException => (StatusCodes.Status404NotFound, "Device not found"),
            DeveloperModeHiddenException => (StatusCodes.Status409Conflict, "Developer mode toggle hidden"),
            DeveloperImagesMissingException => (StatusCodes.Status424FailedDependency, "Developer images missing"),
            RouteSimulationAlreadyRunningException => (StatusCodes.Status409Conflict, "Route simulation already running"),
            RouteSimulationNotFoundException => (StatusCodes.Status404NotFound, "Route simulation not found"),
            RouteSimulationInvalidException => (StatusCodes.Status400BadRequest, "Invalid route simulation request"),
            PyMobileDeviceException => (StatusCodes.Status502BadGateway, "pymobiledevice3 command failed"),
            _ => (StatusCodes.Status500InternalServerError, "An unexpected error occurred"),
        };

        if (statusCode == StatusCodes.Status500InternalServerError)
            logger.LogError(exception, "Unhandled exception while processing {Path}", httpContext.Request.Path);
        else
            logger.LogInformation("Request to {Path} failed: {Message}", httpContext.Request.Path, exception.Message);

        var detail = exception is PyMobileDeviceException { Stderr: { Length: > 0 } stderr }
            ? $"{exception.Message} ({stderr.Trim()})"
            : exception.Message;

        httpContext.Response.StatusCode = statusCode;
        await httpContext.Response.WriteAsJsonAsync(new ProblemDetails {
            Status = statusCode,
            Title = title,
            Detail = detail,
        }, cancellationToken);

        return true;
    }
}
