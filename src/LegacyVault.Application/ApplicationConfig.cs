using LegacyVault.Application.Services;
using Microsoft.Extensions.DependencyInjection;

namespace LegacyVault.Application;

public static class ApplicationConfig
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        // TimeProvider đăng ký một lần ở InfrastructureConfig.
        services.AddScoped<IVaultService, VaultService>();
        services.AddScoped<IPlanSetupService, PlanSetupService>();
        services.AddScoped<IAssetService, AssetService>();
        services.AddScoped<IContainerService, ContainerService>();
        services.AddScoped<IBeneficiaryService, BeneficiaryService>();
        services.AddHandoverApplication();

        // Điểm danh (deadman switch)
        services.AddScoped<ICheckInService, CheckInService>();
        services.AddScoped<ICheckInScheduler, CheckInScheduler>();
        services.AddScoped<ICheckInJob, CheckInJob>();

        // Hồ sơ chứng tử (F2B)
        services.AddScoped<IDeathCaseNoticeJob, DeathCaseNoticeJob>();
        services.AddScoped<IDeathCaseService, DeathCaseService>();
        services.AddScoped<IDeathCaseReviewService, DeathCaseReviewService>();
        services.AddScoped<IRetentionJob, RetentionJob>();
        services.AddScoped<IExecutorManagementService, ExecutorManagementService>();
        services.AddScoped<IExecutorInvitationJob, ExecutorInvitationJob>();
        services.AddScoped<IIncidentService, IncidentService>();
        services.AddScoped<IHoldService, HoldService>();
        services.AddScoped<IHoldReader, HoldReader>();
        services.AddScoped<IAdminService, AdminService>();
        services.AddScoped<IOperationsRecorder, OperationsRecorder>();
        services.AddScoped<IIncidentJob, IncidentJob>();
        services.AddScoped<IOwnerObjectionService, OwnerObjectionService>();
        services.AddScoped<IDeathCaseSupplementJob, DeathCaseSupplementJob>();
        services.AddScoped<IVerifierObjectionService, VerifierObjectionService>();
        services.AddScoped<IDeathCaseVerifierPicker, DeathCaseVerifierPicker>();
        services.AddScoped<IDeathCaseAssignmentJob, DeathCaseAssignmentJob>();

        // Tài khoản: đăng ký, xác minh email, đăng nhập, phiên, quên và đặt lại mật khẩu
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IRegistrationService, RegistrationService>();
        services.AddScoped<IPasswordResetService, PasswordResetService>();

        // Đăng nhập Google
        services.AddScoped<IGoogleAuthService, GoogleAuthService>();
        services.AddScoped<LoginSessionIssuer>();
        services.AddScoped<LoginThrottleGuard>();

        // Xác minh danh tính (eKYC)
        services.AddScoped<IEkycService, EkycService>();

        // Nhật ký hoạt động (SEC-13)
        services.AddScoped<IAuditLogService, AuditLogService>();

        // Executor theo dõi tiến độ bàn giao
        services.AddScoped<IHandoverProgressService, HandoverProgressService>();
        return services;
    }
}
