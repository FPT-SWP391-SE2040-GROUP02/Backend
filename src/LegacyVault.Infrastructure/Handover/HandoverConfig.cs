using LegacyVault.Application.Notifications;
using LegacyVault.Application.Repositories;
using LegacyVault.Application.Services;
using LegacyVault.Infrastructure.Notifications;
using LegacyVault.Infrastructure.Repositories;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace LegacyVault.Infrastructure.Handover;

/// <summary>
/// Đăng ký phần hạ tầng của giai đoạn nhận tài sản. Gọi một dòng từ InfrastructureConfig,
/// đặt sau AddEmailServices (cần SmtpMailSender và SmtpSettings).
/// </summary>
public static class HandoverConfig
{
    public static IServiceCollection AddHandover(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<IHandoverRepository, HandoverRepository>();

        // Công tắc bỏ qua xác minh: sai môi trường thì API không khởi động (HandoverSettingsValidator).
        services.AddOptions<HandoverSettings>()
            .Bind(configuration.GetSection(HandoverSettings.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<HandoverSettings>, HandoverSettingsValidator>();
        services.AddSingleton<IHandoverPolicy, HandoverPolicy>();
        services.AddHostedService<HandoverStartupLogger>();

        services.Configure<HandoverJobSettings>(configuration.GetSection(HandoverJobSettings.SectionName));
        services.AddHostedService<HandoverMonitor>();

        // Chọn bộ gửi lúc cần dùng: có Email:Smtp đầy đủ thì gửi thật, không thì chỉ ghi log (như EmailConfig).
        services.AddSingleton<IHandoverInvitationSender>(provider => ChooseSender(provider));
        return services;
    }

    private static IHandoverInvitationSender ChooseSender(IServiceProvider provider)
    {
        SmtpSettings smtp = provider.GetRequiredService<IOptions<SmtpSettings>>().Value;
        if (smtp.IsConfigured)
        {
            return ActivatorUtilities.CreateInstance<SmtpHandoverInvitationSender>(provider);
        }
        return ActivatorUtilities.CreateInstance<LogHandoverInvitationSender>(provider);
    }
}
