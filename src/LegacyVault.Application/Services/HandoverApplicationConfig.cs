using Microsoft.Extensions.DependencyInjection;

namespace LegacyVault.Application.Services;

/// <summary>Đăng ký service của giai đoạn nhận tài sản. ApplicationConfig chỉ gọi một dòng vào đây.</summary>
public static class HandoverApplicationConfig
{
    /// <summary>Đăng ký các service, việc nền và bộ dựng văn bản của luồng bàn giao.</summary>
    public static IServiceCollection AddHandoverApplication(this IServiceCollection services)
    {
        services.AddScoped<IHandoverStarter, HandoverStarter>();
        services.AddScoped<IHandoverService, HandoverService>();
        services.AddScoped<IInvitationMailCheckService, InvitationMailCheckService>();
        services.AddScoped<IHandoverPaperWriter, HandoverPaperWriter>();
        services.AddScoped<IHandoverDocumentNotifier, HandoverDocumentNotifier>();
        services.AddScoped<IHandoverDocumentService, HandoverDocumentService>();
        services.AddScoped<IHandoverSubmissionService, HandoverSubmissionService>();
        services.AddScoped<IHandoverSubmissionReviewService, HandoverSubmissionReviewService>();
        services.AddScoped<IHandoverDownloadService, HandoverDownloadService>();
        services.AddScoped<IHandoverJob, HandoverJob>();
        services.AddScoped<IHandoverInvitationRetryJob, HandoverInvitationRetryJob>();
        services.AddScoped<IHandoverKeyMailService, HandoverKeyMailService>();
        services.AddScoped<IExecutorDeclinePaperService, ExecutorDeclinePaperService>();
        return services;
    }
}
