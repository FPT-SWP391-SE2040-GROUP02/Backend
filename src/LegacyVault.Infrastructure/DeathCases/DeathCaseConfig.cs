using LegacyVault.Application.Ekyc;
using LegacyVault.Application.Notifications;
using LegacyVault.Application.Repositories;
using LegacyVault.Application.Security;
using LegacyVault.Application.Services;
using LegacyVault.Infrastructure.Ekyc;
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
        services.AddScoped<IOwnerFaceVerifier, OwnerFaceVerifier>();
        services.AddScoped<IDeathCaseAssignmentRepository, DeathCaseAssignmentRepository>();
        // AI nhận diện trang giấy có phải giấy chứng tử khi Executor gửi hồ sơ.
        // DEATH-03: bộ đếm nhập sai mật khẩu xác nhận duyệt/từ chối của Verifier.
        services.AddScoped<IVerifierSigningGuardRepository, VerifierSigningGuardRepository>();

        // DATA-02, D6: mã hóa phần khai báo nhạy cảm, dùng chung DataKeyProtector của tài sản.
        services.AddSingleton<IDeathCaseDataProtector, DeathCaseDataProtector>();

        services.Configure<DeathCaseJobSettings>(configuration.GetSection(DeathCaseJobSettings.SectionName));

        // D6: job giao Verifier cho hồ sơ trống, nhắc trước hạn và giao lại khi quá hạn.
        services.AddHostedService<DeathCaseAssignmentMonitor>();
        services.AddScoped<ISupplementOverdueRepository, SupplementOverdueRepository>();
        services.AddHostedService<DeathCaseSupplementMonitor>();

        // Chọn bộ gửi lúc cần dùng: có Email:Smtp đầy đủ thì gửi thật, không thì chỉ ghi log (như HandoverConfig).
        services.AddSingleton<IDeathCaseVerifierReminderSender>(provider => ChooseReminderSender(provider));
        services.AddSingleton<IDeathCaseExecutorNoticeSender>(provider => ChooseExecutorNoticeSender(provider));
        return services;
    }

    private static IDeathCaseVerifierReminderSender ChooseReminderSender(IServiceProvider provider)
    {
        SmtpSettings smtp = provider.GetRequiredService<IOptions<SmtpSettings>>().Value;
        if (smtp.IsConfigured)
        {
            return ActivatorUtilities.CreateInstance<SmtpDeathCaseVerifierReminderSender>(provider);
        }
        return ActivatorUtilities.CreateInstance<LogDeathCaseVerifierReminderSender>(provider);
    }

    private static IDeathCaseExecutorNoticeSender ChooseExecutorNoticeSender(IServiceProvider provider)
    {
        SmtpSettings smtp = provider.GetRequiredService<IOptions<SmtpSettings>>().Value;
        if (smtp.IsConfigured)
        {
            return ActivatorUtilities.CreateInstance<SmtpDeathCaseExecutorNoticeSender>(provider);
        }
        return ActivatorUtilities.CreateInstance<LogDeathCaseExecutorNoticeSender>(provider);
    }
}
