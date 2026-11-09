using Microsoft.Extensions.DependencyInjection;

namespace LegacyVault.Application.Services;

/// <summary>Đăng ký service của giai đoạn nhận tài sản. ApplicationConfig chỉ gọi một dòng vào đây.</summary>
public static class HandoverApplicationConfig
{
    public static IServiceCollection AddHandoverApplication(this IServiceCollection services)
    {
        services.AddScoped<IHandoverStarter, HandoverStarter>();
        services.AddScoped<IHandoverService, HandoverService>();
        services.AddScoped<IInvitationMailCheckService, InvitationMailCheckService>();
        services.AddScoped<IHandoverSubmissionService, HandoverSubmissionService>();
        services.AddScoped<IHandoverSubmissionReviewService, HandoverSubmissionReviewService>();
        services.AddScoped<IHandoverDownloadService, HandoverDownloadService>();
        services.AddScoped<IHandoverJob, HandoverJob>();
        services.AddScoped<IHandoverIdentityService, HandoverIdentityService>();
        services.AddScoped<IHandoverInvitationService, HandoverInvitationService>();
        services.AddScoped<IHandoverKeyMailService, HandoverKeyMailService>();
        services.AddScoped<IExecutorDeclinePaperService, ExecutorDeclinePaperService>();
        return services;
    }
}
