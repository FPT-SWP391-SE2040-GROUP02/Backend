using LegacyVault.Application.Exceptions;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace LegacyVault.Api.Exceptions;

public sealed class GlobalExceptionHandler : IExceptionHandler
{
    private readonly IProblemDetailsService _problemDetails;
    private readonly ILogger<GlobalExceptionHandler> _logger;

    public GlobalExceptionHandler(
        IProblemDetailsService problemDetails,
        ILogger<GlobalExceptionHandler> logger)
    {
        _problemDetails = problemDetails;
        _logger = logger;
    }

    public async ValueTask<bool> TryHandleAsync(
        HttpContext context, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is ApiException apiEx)
        {
            context.Response.StatusCode = apiEx.StatusCode;
            var problem = new ProblemDetails
            {
                Status = apiEx.StatusCode,
                Title = apiEx.Message
            };
            problem.Extensions["code"] = apiEx.ErrorCode;
            problem.Extensions["traceId"] = context.TraceIdentifier;
            foreach (var kvp in apiEx.ExtraData)
            {
                problem.Extensions[kvp.Key] = kvp.Value;
            }

            if (!await _problemDetails.TryWriteAsync(new ProblemDetailsContext
            {
                HttpContext = context,
                ProblemDetails = problem
            }))
            {
                await context.Response.WriteAsJsonAsync(problem, cancellationToken);
            }
            return true;
        }

        // Không ghi nội dung request, connection string hoặc exception message có thể chứa secrets.
        _logger.LogError("Unhandled {ExceptionType}; trace {TraceId}",
            exception.GetType().Name, context.TraceIdentifier);
        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
        var defaultProblem = new ProblemDetails
        {
            Status = StatusCodes.Status500InternalServerError,
            Title = "Có lỗi hệ thống. Vui lòng thử lại sau."
        };
        defaultProblem.Extensions["code"] = "INTERNAL_ERROR";
        defaultProblem.Extensions["traceId"] = context.TraceIdentifier;
        if (!await _problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = context,
            ProblemDetails = defaultProblem
        }))
        {
            await context.Response.WriteAsJsonAsync(defaultProblem, cancellationToken);
        }
        return true;
    }
}
