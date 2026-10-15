using LegacyVault.Application.Notifications;
using LegacyVault.Application.Repositories;
using LegacyVault.Application.Security;
using LegacyVault.Infrastructure.Jobs;
using LegacyVault.Infrastructure.Notifications;
using LegacyVault.Infrastructure.Repositories;
using LegacyVault.Infrastructure.Security;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace LegacyVault.Infrastructure.DeathCases;

/// <summary>
/// Đăng ký phần hạ tầng của hồ sơ chứng tử (F2B). Gọi một dòng từ InfrastructureConfig,
/// đặt sau HandoverConfig.AddHandover (cần DataKeyProtector, SmtpMailSender và SmtpSettings đã đăng ký).
/// </summary>
public static class DeathCaseConfig
{
    public static IServiceCollection AddDeathCases(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<IDeathCaseRepository, DeathCaseRepository>();
        services.AddScoped<IDeathCaseReviewRepository, DeathCaseReviewRepository>();
        services.AddScoped<IDeathCaseAssignmentRepository, DeathCaseAssignmentRepository>();

        // DATA-02, D6: mã hóa phần khai báo nhạy cảm, dùng chung DataKeyProtector của tài sản.
        services.AddSingleton<IDeathCaseDataProtector, DeathCaseDataProtector>();

        // D3: job gửi thông báo Owner theo outbox.
        services.Configure<DeathCaseJobSettings>(configuration.GetSection(DeathCaseJobSettings.SectionName));
        services.AddHostedService<DeathCaseNoticeMonitor>();

        // Chọn bộ gửi lúc cần dùng: có Email:Smtp đầy đủ thì gửi thật, không thì chỉ ghi log (như HandoverConfig).
        services.AddSingleton<IDeathCaseNoticeSender>(provider => ChooseSender(provider));
        return services;
    }

    private static IDeathCaseNoticeSender ChooseSender(IServiceProvider provider)
    {
        SmtpSettings smtp = provider.GetRequiredService<IOptions<SmtpSettings>>().Value;
        if (smtp.IsConfigured)
        {
            return ActivatorUtilities.CreateInstance<SmtpDeathCaseNoticeSender>(provider);
        }
        return ActivatorUtilities.CreateInstance<LogDeathCaseNoticeSender>(provider);
    }
}
