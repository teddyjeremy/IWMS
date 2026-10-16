using IWMS.Domain.Common;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace IWMS.Api.Errors;

public class GlobalExceptionHandler : IExceptionHandler
{
    private readonly ILogger<GlobalExceptionHandler> _logger;

    public GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger)
    {
        _logger = logger;
    }

    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        var (status, title, detail) = exception switch
        {
            DomainException =>
                (StatusCodes.Status400BadRequest, "Business rule violated", exception.Message),
            DbUpdateConcurrencyException =>
                (StatusCodes.Status409Conflict, "Conflict",
                 "This record was changed by someone else. Reload it and try again."),
            DbUpdateException =>
                (StatusCodes.Status409Conflict, "Conflict",
                 "The request conflicts with existing data, for example a duplicate."),
            _ =>
                (StatusCodes.Status500InternalServerError, "Unexpected error",
                 "An unexpected error occurred.")
        };

        if (status == StatusCodes.Status500InternalServerError)
            _logger.LogError(exception, "Unhandled exception on {Path}", httpContext.Request.Path);
        else
            _logger.LogWarning("Request rejected ({Status}): {Message}", status, exception.Message);

        httpContext.Response.StatusCode = status;
        await httpContext.Response.WriteAsJsonAsync(
            new ProblemDetails { Status = status, Title = title, Detail = detail },
            cancellationToken);

        return true;
    }
}