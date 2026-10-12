using LegacyVault.Api.Exceptions;

namespace LegacyVault.Api.Config;

public static class ApiConfig
{
    public static IServiceCollection AddApiConfig(this IServiceCollection services)
    {
        services.AddControllers();
        services.AddProblemDetails(options =>
            options.CustomizeProblemDetails = context =>
                context.ProblemDetails.Extensions["traceId"] = context.HttpContext.TraceIdentifier);
        services.AddExceptionHandler<GlobalExceptionHandler>();
        services.AddOpenApi();
        return services;
    }
}
