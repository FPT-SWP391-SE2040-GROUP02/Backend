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
        services.AddScoped<IWillIdentityService, WillIdentityService>();
        services.AddScoped<IAssetService, AssetService>();
        services.AddScoped<IContainerService, ContainerService>();
        services.AddScoped<IBeneficiaryService, BeneficiaryService>();
        services.AddScoped<IVaultPinService, VaultPinService>();
        services.AddScoped<ExecutorKeyIssuer>();
        services.AddScoped<IExecutorKeyService, ExecutorKeyService>();
        services.AddScoped<ISystemExecutorDirectoryService, SystemExecutorDirectoryService>();
        services.AddHandoverApplication();

        services.AddScoped<IExecutorAssetService, ExecutorAssetService>();
        services.AddScoped<IEntitlementExpiryJob, EntitlementExpiryJob>();

        // Hồ sơ chứng tử (F2B)
        services.AddScoped<IDeathCaseService, DeathCaseService>();
        services.AddScoped<IDeathCaseReviewService, DeathCaseReviewService>();
        services.AddScoped<IAdministrativeUnitService, AdministrativeUnitService>();
        services.AddScoped<ISigningPasswordChecker, SigningPasswordChecker>();
        services.AddScoped<IRetentionJob, RetentionJob>();
        services.AddScoped<IExecutorManagementService, ExecutorManagementService>();
        services.AddScoped<IExecutorInvitationJob, ExecutorInvitationJob>();
        services.AddScoped<IAdminService, AdminService>();
        services.AddScoped<ISystemConfigService, SystemConfigService>();
        services.AddScoped<IDeathCaseSupplementJob, DeathCaseSupplementJob>();
        services.AddScoped<IDeathCaseVerifierPicker, DeathCaseVerifierPicker>();
        services.AddScoped<IDeathCaseAssignmentJob, DeathCaseAssignmentJob>();

        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IRegistrationService, RegistrationService>();
        services.AddScoped<IPasswordResetService, PasswordResetService>();
        services.AddScoped<ILoginMethodService, LoginMethodService>();
        services.AddScoped<IAvatarService, AvatarService>();
        services.AddScoped<INotaryCardService, NotaryCardService>();
        services.AddScoped<ITwoFactorService, TwoFactorService>();
        services.AddScoped<IAccountPhoneService, AccountPhoneService>();
        services.AddScoped<IPlanActivatedNotifier, PlanActivatedNotifier>();

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
        services.AddScoped<IHandoverTemplateService, HandoverTemplateService>();
        services.AddScoped<IEmailTemplateService, EmailTemplateService>();
        // Công cụ thử nghiệm của Admin (controller chỉ mở ở Development)
        services.AddScoped<IDevToolsService, DevToolsService>();
        // Chế độ bảo trì: chặn tài khoản không phải Admin ở đăng nhập, làm mới phiên và middleware API
        services.AddScoped<MaintenanceModeGuard>();
        return services;
    }
}
