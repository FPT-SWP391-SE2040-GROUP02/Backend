using LegacyVault.Application.Exceptions;
using LegacyVault.Application.Mappings;
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
        var problem = exception switch
        {
            AppException appException => FromAppException(appException),
            BadHttpRequestException { StatusCode: StatusCodes.Status413PayloadTooLarge } =>
                Problem(StatusCodes.Status413PayloadTooLarge, ErrorCode.FileTooLarge, "Tệp vượt quá dung lượng cho phép."),
            _ => null,
        };

        if (problem is null)
        {
            // Không ghi nội dung request, connection string hoặc exception message có thể chứa secrets.
            logger.LogError("Unhandled {ExceptionType}; trace {TraceId}",
                exception.GetType().Name, context.TraceIdentifier);
            problem = Problem(StatusCodes.Status500InternalServerError, ErrorCode.InternalError,
                "Có lỗi hệ thống. Vui lòng thử lại sau.");
        }

        problem.Extensions["traceId"] = context.TraceIdentifier;
        context.Response.StatusCode = problem.Status!.Value;
        if (!await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = context,
            ProblemDetails = problem,
            Exception = exception,
        }))
        {
            await context.Response.WriteAsJsonAsync(problem, cancellationToken);
        }
        return true;
    }

    private static ProblemDetails FromAppException(AppException exception)
    {
        var status = exception.Type switch
        {
            AppErrorType.Validation => StatusCodes.Status400BadRequest,
            AppErrorType.Unauthorized => StatusCodes.Status401Unauthorized,
            AppErrorType.Forbidden => StatusCodes.Status403Forbidden,
            AppErrorType.NotFound => StatusCodes.Status404NotFound,
            AppErrorType.Conflict => StatusCodes.Status409Conflict,
            AppErrorType.PayloadTooLarge => StatusCodes.Status413PayloadTooLarge,
            _ => StatusCodes.Status500InternalServerError,
        };
        var problem = Problem(status, exception.Code, exception.Message);
        foreach (var (key, value) in exception.Extensions)
        {
            problem.Extensions[key] = value;
        }
        return problem;
    }

    public static ProblemDetails Problem(int status, ErrorCode code, string title)
    {
        var problem = new ProblemDetails { Status = status, Title = title };
        problem.Extensions["code"] = code.ToApiString();
        return problem;
    }
}
