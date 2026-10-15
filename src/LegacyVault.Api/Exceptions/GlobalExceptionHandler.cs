using LegacyVault.Application.Exceptions;
using LegacyVault.Application.Mappings;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LegacyVault.Api.Exceptions;

/// <summary>
/// Đổi exception thành ProblemDetails (docs/api-contract.md mục 2):
/// - ApiException (module Auth): status/code theo exception, kèm ExtraData.
/// - AppException (kho, tài sản, gói): status theo AppErrorType, code UPPER_SNAKE_CASE, kèm Extensions.
/// - Request vượt giới hạn kích thước → 413 FILE_TOO_LARGE.
/// - DbUpdateConcurrencyException (UpdatedAt là concurrency token) → 409 EDIT_CONFLICT.
/// - Còn lại → 500 INTERNAL_ERROR.
/// </summary>
public sealed class GlobalExceptionHandler(
    IProblemDetailsService problemDetails,
    ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext context, Exception exception, CancellationToken cancellationToken)
    {
        ProblemDetails problem;
        if (exception is ApiException apiException)
        {
            problem = FromApiException(apiException);
        }
        else if (exception is AppException appException)
        {
            problem = FromAppException(appException);
        }
        else if (exception is BadHttpRequestException badRequest
            && badRequest.StatusCode == StatusCodes.Status413PayloadTooLarge)
        {
            problem = Problem(StatusCodes.Status413PayloadTooLarge, ErrorCode.FileTooLarge,
                "Tệp vượt quá dung lượng cho phép.");
        }
        else if (exception is BadHttpRequestException invalidRequest
            && invalidRequest.StatusCode == StatusCodes.Status400BadRequest)
        {
            problem = Problem(StatusCodes.Status400BadRequest, ErrorCode.ValidationError,
                "Dữ liệu gửi lên không hợp lệ.");
        }
        else if (exception is DbUpdateConcurrencyException)
        {
            // Request khác vừa sửa cùng bản ghi (UPDATE ... WHERE UpdatedAt = giá trị cũ không khớp dòng nào).
            problem = Problem(StatusCodes.Status409Conflict, ErrorCode.EditConflict,
                "Dữ liệu vừa được sửa ở nơi khác. Vui lòng tải lại rồi sửa tiếp.");
        }
        else
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
        }))
        {
            await context.Response.WriteAsJsonAsync(problem, options: null,
                contentType: "application/problem+json", cancellationToken: cancellationToken);
        }
        return true;
    }

    private static ProblemDetails FromApiException(ApiException exception)
    {
        ProblemDetails problem = new ProblemDetails
        {
            Status = exception.StatusCode,
            Title = exception.Message,
        };
        problem.Extensions["code"] = exception.ErrorCode;
        foreach (KeyValuePair<string, object?> item in exception.ExtraData)
        {
            problem.Extensions[item.Key] = item.Value;
        }
        return problem;
    }

    private static ProblemDetails FromAppException(AppException exception)
    {
        int status;
        if (exception.Type == AppErrorType.Validation)
        {
            status = StatusCodes.Status400BadRequest;
        }
        else if (exception.Type == AppErrorType.Unauthorized)
        {
            status = StatusCodes.Status401Unauthorized;
        }
        else if (exception.Type == AppErrorType.Forbidden)
        {
            status = StatusCodes.Status403Forbidden;
        }
        else if (exception.Type == AppErrorType.NotFound)
        {
            status = StatusCodes.Status404NotFound;
        }
        else if (exception.Type == AppErrorType.Conflict)
        {
            status = StatusCodes.Status409Conflict;
        }
        else if (exception.Type == AppErrorType.PayloadTooLarge)
        {
            status = StatusCodes.Status413PayloadTooLarge;
        }
        else if (exception.Type == AppErrorType.ServiceUnavailable)
        {
            status = StatusCodes.Status503ServiceUnavailable;
        }
        else
        {
            status = StatusCodes.Status500InternalServerError;
        }

        ProblemDetails problem = Problem(status, exception.Code, exception.Message);
        foreach (KeyValuePair<string, object?> item in exception.Extensions)
        {
            problem.Extensions[item.Key] = item.Value;
        }
        return problem;
    }

    /// <summary>ProblemDetails có status, title và code (UPPER_SNAKE_CASE). Dùng chung cho handler lỗi và 401 của JWT.</summary>
    public static ProblemDetails Problem(int status, ErrorCode code, string title)
    {
        ProblemDetails problem = new ProblemDetails { Status = status, Title = title };
        problem.Extensions["code"] = code.ToApiString();
        return problem;
    }
}
