using LegacyVault.Api.Exceptions;
using LegacyVault.Api.Security;
using LegacyVault.Application.Exceptions;
using LegacyVault.Application.Mappings;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Mvc;

namespace LegacyVault.Api.Config;

public static class ApiConfig
{
    public static IServiceCollection AddApiConfig(this IServiceCollection services)
    {
        services.AddControllersWithViews()
            .ConfigureApiBehaviorOptions(options =>
                options.InvalidModelStateResponseFactory = CreateValidationProblem);
        services.AddProblemDetails(options =>
            options.CustomizeProblemDetails = context =>
                context.ProblemDetails.Extensions["traceId"] = context.HttpContext.TraceIdentifier);
        services.AddExceptionHandler<GlobalExceptionHandler>();
        services.AddOpenApi(AuthOpenApiConfig.Configure);
        services.AddSingleton<AuthCookieWriter>();
        services.AddAuthSecurity();

        // Cấu hình JWT của Auth (InfrastructureConfig.AddJwtBearer): bổ sung OnChallenge để 401 trả
        // ProblemDetails có code SESSION_EXPIRED, khớp docs/api-contract.md mục 2 và 3.
        // Đặt ở Api vì cần GlobalExceptionHandler.Problem(...) để cùng format với các lỗi khác.
        services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
        {
            if (options.Events == null)
            {
                options.Events = new JwtBearerEvents();
            }
            options.Events.OnChallenge = WriteSessionExpiredAsync;
            // Tài khoản bị khóa/vô hiệu hóa: chặn ngay ở request kế tiếp, không chờ access token hết hạn.
            options.Events.OnTokenValidated = ActiveUserTokenValidator.ValidateAsync;
        });
        return services;
    }

    /// <summary>
    /// Không có token / token sai / hết hạn trên endpoint [Authorize] → 401 SESSION_EXPIRED.
    /// Token hợp lệ nhưng tài khoản bị khóa/vô hiệu hóa → 403 ACCOUNT_NOT_ACTIVE.
    /// </summary>
    private static async Task WriteSessionExpiredAsync(JwtBearerChallengeContext context)
    {
        // Chặn response 401 mặc định (body rỗng + header WWW-Authenticate) để tự ghi ProblemDetails.
        context.HandleResponse();

        HttpContext httpContext = context.HttpContext;
        ProblemDetails problem = GlobalExceptionHandler.Problem(StatusCodes.Status401Unauthorized,
            ErrorCode.SessionExpired, "Phiên đăng nhập đã hết hạn. Vui lòng đăng nhập lại.");
        if (httpContext.Items.ContainsKey(ActiveUserTokenValidator.InactiveAccountItem))
        {
            // Token hợp lệ nhưng tài khoản đã bị khóa/vô hiệu hóa: báo rõ để FE không coi là phiên hết hạn.
            problem = GlobalExceptionHandler.Problem(StatusCodes.Status403Forbidden,
                ErrorCode.AccountNotActive, "Tài khoản đang bị khóa hoặc vô hiệu hóa.");
        }
        problem.Extensions["traceId"] = httpContext.TraceIdentifier;
        httpContext.Response.StatusCode = problem.Status!.Value;

        IProblemDetailsService problemDetails = httpContext.RequestServices.GetRequiredService<IProblemDetailsService>();
        bool written = await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = problem,
        });
        if (!written)
        {
            await httpContext.Response.WriteAsJsonAsync(problem, options: null,
                contentType: "application/problem+json");
        }
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
