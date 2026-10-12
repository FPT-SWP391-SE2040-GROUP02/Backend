using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace LegacyVault.Api.Exceptions;

public sealed class GlobalExceptionHandler(
    IProblemDetailsService problemDetails,
    ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext context, Exception exception, CancellationToken cancellationToken)
    {
        // Không ghi nội dung request, connection string hoặc exception message có thể chứa secrets.
        logger.LogError("Unhandled {ExceptionType}; trace {TraceId}",
            exception.GetType().Name, context.TraceIdentifier);
        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
        var problem = new ProblemDetails
        {
            Status = StatusCodes.Status500InternalServerError,
            Title = "Có lỗi hệ thống. Vui lòng thử lại sau."
        };
        problem.Extensions["traceId"] = context.TraceIdentifier;
        if (!await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = context,
            ProblemDetails = problem
        }))
        {
            await context.Response.WriteAsJsonAsync(problem, cancellationToken);
        }
        return true;
    }
}
