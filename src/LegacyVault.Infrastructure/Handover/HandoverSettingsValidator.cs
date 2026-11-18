using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace LegacyVault.Infrastructure.Handover;

/// <summary>Chặn cấu hình tắt xác minh danh tính ở môi trường không phải Development.</summary>
public sealed class HandoverSettingsValidator : IValidateOptions<HandoverSettings>
{
    private readonly IHostEnvironment environment;

    public HandoverSettingsValidator(IHostEnvironment environment)
    {
        this.environment = environment;
    }

    /// <summary>Tắt xác minh danh tính (RequireIdentityVerification là false) chỉ được ở môi trường Development; môi trường khác thì ứng dụng không khởi động.</summary>
    public ValidateOptionsResult Validate(string? name, HandoverSettings options)
    {
        if (options.RequireIdentityVerification)
        {
            return ValidateOptionsResult.Success;
        }

        bool isDevelopment = string.Equals(environment.EnvironmentName, Environments.Development, StringComparison.OrdinalIgnoreCase);
        if (isDevelopment)
        {
            return ValidateOptionsResult.Success;
        }

        return ValidateOptionsResult.Fail(
            "Handover:RequireIdentityVerification=false chỉ được dùng ở môi trường Development.");
    }
}
