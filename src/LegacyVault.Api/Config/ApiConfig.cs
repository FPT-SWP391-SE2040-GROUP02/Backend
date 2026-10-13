using LegacyVault.Api.Exceptions;
using LegacyVault.Application.Exceptions;
using LegacyVault.Application.Mappings;
using Microsoft.AspNetCore.Mvc;

namespace LegacyVault.Api.Config;

public static class ApiConfig
{
    public static IServiceCollection AddApiConfig(this IServiceCollection services)
    {
        services.AddControllers()
            .ConfigureApiBehaviorOptions(options =>
                options.InvalidModelStateResponseFactory = CreateValidationProblem);
        services.AddProblemDetails(options =>
            options.CustomizeProblemDetails = context =>
                context.ProblemDetails.Extensions["traceId"] = context.HttpContext.TraceIdentifier);
        services.AddExceptionHandler<GlobalExceptionHandler>();
        services.AddOpenApi();
        return services;
    }

    /// <summary>
    /// Lỗi model binding/DataAnnotations → 400 VALIDATION_ERROR, errors là object theo tên trường camelCase
    /// (docs/api-contract.md mục 2).
    /// </summary>
    private static IActionResult CreateValidationProblem(ActionContext context)
    {
        var errors = context.ModelState
            .Where(entry => entry.Value is { Errors.Count: > 0 })
            .ToDictionary(
                entry => ToCamelCase(entry.Key),
                entry => entry.Value!.Errors
                    .Select(e => MessageOrDefault(e.ErrorMessage))
                    .ToArray());

        var problem = new ValidationProblemDetails(errors)
        {
            Status = StatusCodes.Status400BadRequest,
            Title = "Dữ liệu gửi lên không hợp lệ.",
        };
        problem.Extensions["code"] = ErrorCode.ValidationError.ToApiString();
        problem.Extensions["traceId"] = context.HttpContext.TraceIdentifier;
        return new BadRequestObjectResult(problem) { ContentTypes = { "application/problem+json" } };
    }

    private static string ToCamelCase(string key)
    {
        // "$.name" (lỗi JSON) → "name"; "Name" → "name"; "request.Name" giữ phần sau dấu chấm cuối.
        var trimmed = key.TrimStart('$', '.');
        var lastDot = trimmed.LastIndexOf('.');
        string field = trimmed;
        if (lastDot >= 0)
        {
            field = trimmed.Substring(lastDot + 1);
        }
        if (field.Length == 0)
        {
            return key;
        }
        return char.ToLowerInvariant(field[0]) + field.Substring(1);
    }

    private static string MessageOrDefault(string? message)
    {
        if (string.IsNullOrEmpty(message))
        {
            return "Giá trị không hợp lệ.";
        }
        return message;
    }
}
