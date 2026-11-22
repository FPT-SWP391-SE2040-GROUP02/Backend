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
    /// <summary>Đăng ký repository, chính sách, các vòng quét nền và bộ gửi thư của luồng bàn giao.</summary>
    public static IServiceCollection AddHandover(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<IHandoverRepository, HandoverRepository>();
        services.AddScoped<IHandoverRecordRepository, HandoverRecordRepository>();
        // Khóa ký sai nhiều lần của luật sư và Người thực thi.
        services.AddScoped<IVerifierSigningGuardRepository, VerifierSigningGuardRepository>();
        services.AddScoped<IHandoverSubmissionRepository, HandoverSubmissionRepository>();
        services.AddScoped<IHandoverContactReader, HandoverContactReader>();

        // Công tắc bỏ qua xác minh: sai môi trường thì API không khởi động (HandoverSettingsValidator).
        services.AddOptions<HandoverSettings>()
            .Bind(configuration.GetSection(HandoverSettings.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<HandoverSettings>, HandoverSettingsValidator>();
        services.AddSingleton<IHandoverPolicy, HandoverPolicy>();
        services.AddHostedService<HandoverStartupLogger>();

        services.Configure<HandoverJobSettings>(configuration.GetSection(HandoverJobSettings.SectionName));
        services.AddHostedService<HandoverMonitor>();

        // Lưu và xóa dữ liệu sau bàn giao.
        services.AddScoped<IRetentionRepository, RetentionRepository>();
        services.Configure<RetentionJobSettings>(configuration.GetSection(RetentionJobSettings.SectionName));
        services.AddHostedService<RetentionMonitor>();
        services.AddSingleton<IRetentionNoticeSender>(ChooseRetentionSender);

        // Chọn bộ gửi lúc cần dùng: có Email:Smtp đầy đủ thì gửi thật, không thì chỉ ghi log (như EmailConfig).
        services.AddSingleton<IHandoverInvitationSender>(ChooseSender);
        services.AddSingleton<IPlanActivatedNoticeSender>(ChooseActivatedNoticeSender);
        services.AddSingleton<IHandoverRequestNoticeSender>(ChooseRequestNoticeSender);
        services.AddSingleton<IHandoverKeySender>(ChooseKeySender);
        return services;
    }

    /// <summary>Thư gửi khóa mở gói: có cấu hình SMTP thì gửi thật, không thì chỉ ghi log.</summary>
    private static IHandoverKeySender ChooseKeySender(IServiceProvider provider)
    {
        SmtpSettings smtp = provider.GetRequiredService<IOptions<SmtpSettings>>().Value;
        if (smtp.IsConfigured)
        {
            return ActivatorUtilities.CreateInstance<SmtpHandoverKeySender>(provider);
        }
        return ActivatorUtilities.CreateInstance<LogHandoverKeySender>(provider);
    }

    /// <summary>Thư báo có yêu cầu nhận tài sản: có cấu hình SMTP thì gửi thật, không thì chỉ ghi log.</summary>
    private static IHandoverRequestNoticeSender ChooseRequestNoticeSender(IServiceProvider provider)
    {
        SmtpSettings smtp = provider.GetRequiredService<IOptions<SmtpSettings>>().Value;
        if (smtp.IsConfigured)
        {
            return ActivatorUtilities.CreateInstance<SmtpHandoverRequestNoticeSender>(provider);
        }
        return ActivatorUtilities.CreateInstance<LogHandoverRequestNoticeSender>(provider);
    }

    /// <summary>Thư báo kích hoạt kế hoạch: có cấu hình SMTP thì gửi thật, không thì chỉ ghi log.</summary>
    private static IPlanActivatedNoticeSender ChooseActivatedNoticeSender(IServiceProvider provider)
    {
        SmtpSettings smtp = provider.GetRequiredService<IOptions<SmtpSettings>>().Value;
        if (smtp.IsConfigured)
        {
            return ActivatorUtilities.CreateInstance<SmtpPlanActivatedNoticeSender>(provider);
        }
        return ActivatorUtilities.CreateInstance<LogPlanActivatedNoticeSender>(provider);
    }

    /// <summary>Thư nhắc mốc lưu dữ liệu: có cấu hình SMTP thì gửi thật, không thì chỉ ghi log.</summary>
    private static IRetentionNoticeSender ChooseRetentionSender(IServiceProvider provider)
    {
        SmtpSettings smtp = provider.GetRequiredService<IOptions<SmtpSettings>>().Value;
        if (smtp.IsConfigured)
        {
            return ActivatorUtilities.CreateInstance<SmtpRetentionNoticeSender>(provider);
        }
        return ActivatorUtilities.CreateInstance<LogRetentionNoticeSender>(provider);
    }

    /// <summary>Thư mời nhận tài sản: có cấu hình SMTP thì gửi thật, không thì chỉ ghi log.</summary>
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
