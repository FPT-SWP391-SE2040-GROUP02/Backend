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
        services.AddScoped<IVaultPinService, VaultPinService>();
        services.AddHandoverApplication();

        // Điểm danh (deadman switch)
        services.AddScoped<ICheckInService, CheckInService>();
        services.AddSingleton<ISmartwatchPairingStore, SmartwatchPairingStore>();
        services.AddScoped<ICheckInScheduler, CheckInScheduler>();
        services.AddScoped<ICheckInJob, CheckInJob>();
        services.AddScoped<IExecutorCheckInService, ExecutorCheckInService>();

        // Hồ sơ chứng tử (F2B)
        services.AddScoped<IDeathCaseNoticeJob, DeathCaseNoticeJob>();
        services.AddScoped<IDeathCaseService, DeathCaseService>();
        services.AddScoped<DeathCertificateChecker>();
        services.AddScoped<IDeathCaseReviewService, DeathCaseReviewService>();
        services.AddScoped<ISigningPasswordChecker, SigningPasswordChecker>();
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

        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IRegistrationService, RegistrationService>();
        services.AddScoped<IPasswordResetService, PasswordResetService>();
        services.AddScoped<ILoginMethodService, LoginMethodService>();
        services.AddScoped<IAvatarService, AvatarService>();
        services.AddScoped<ITwoFactorService, TwoFactorService>();

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

        // Thanh toán (SRS §8)
        services.AddScoped<IBillingService, BillingService>();
        services.AddScoped<PaymentGranter>();
        services.AddScoped<ISePayWebhookService, SePayWebhookService>();
        services.AddScoped<IAdminPaymentService, AdminPaymentService>();
        // Công cụ thử nghiệm của Admin (controller chỉ mở ở Development)
        services.AddScoped<IDevToolsService, DevToolsService>();
        return services;
    }
}
