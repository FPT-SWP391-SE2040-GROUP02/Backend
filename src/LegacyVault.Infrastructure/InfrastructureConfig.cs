using System.Text;
using LegacyVault.Application.Ekyc;
using LegacyVault.Application.Notifications;
using LegacyVault.Application.Repositories;
using LegacyVault.Application.Security;
using LegacyVault.Application.Services;
using LegacyVault.Application.Storage;
using LegacyVault.Infrastructure.Data;
using LegacyVault.Infrastructure.Ekyc;
using LegacyVault.Infrastructure.Health;
using LegacyVault.Infrastructure.Jobs;
using LegacyVault.Infrastructure.Notifications;
using LegacyVault.Infrastructure.Repositories;
using LegacyVault.Infrastructure.Security;
using LegacyVault.Infrastructure.Storage;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace LegacyVault.Infrastructure;

public static class InfrastructureConfig
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton(TimeProvider.System);

        services.AddOptions<DatabaseSettings>()
            .Bind(configuration.GetSection("ConnectionStrings"))
            .Validate(settings => !string.IsNullOrWhiteSpace(settings.Default),
                "Thiếu ConnectionStrings:Default. Khi chạy local, chọn profile http (Development).")
            .ValidateOnStart();

        services.AddDbContext<AppDbContext>((provider, options) =>
            options.UseSqlServer(provider.GetRequiredService<IOptions<DatabaseSettings>>().Value.Default));
        services.AddHealthChecks()
            .AddCheck<DatabaseHealthCheck>("database", tags: ["ready"]);

        // IUnitOfWork chính là AppDbContext của request: repository và service dùng chung một instance.
        services.AddScoped<IUnitOfWork>(provider => provider.GetRequiredService<AppDbContext>());

        // Repositories
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<ILoginThrottleRepository, LoginThrottleRepository>();
        services.AddScoped<IEmailVerificationTokenRepository, EmailVerificationTokenRepository>();
        services.AddScoped<IPasswordResetTokenRepository, PasswordResetTokenRepository>();
        services.AddScoped<IRefreshTokenRepository, RefreshTokenRepository>();
        services.AddScoped<IVaultRepository, VaultRepository>();
        services.AddScoped<IContainerRepository, ContainerRepository>();
        services.AddScoped<IAssetRepository, AssetRepository>();
        services.AddScoped<IBeneficiaryRepository, BeneficiaryRepository>();
        services.AddScoped<ICheckInRepository, CheckInRepository>();
        services.AddScoped<IAuditLogRepository, AuditLogRepository>();
        services.AddScoped<IHoldRepository, HoldRepository>();
        services.AddScoped<IIncidentRepository, IncidentRepository>();
        services.AddScoped<IAdminUserRepository, AdminUserRepository>();
        services.AddHostedService<IncidentMonitor>();
        services.AddSingleton<IIncidentNoticeSender>(provider =>
        {
            SmtpSettings smtp = provider.GetRequiredService<IOptions<SmtpSettings>>().Value;
            if (smtp.IsConfigured)
            {
                return ActivatorUtilities.CreateInstance<SmtpIncidentNoticeSender>(provider);
            }
            return ActivatorUtilities.CreateInstance<LogIncidentNoticeSender>(provider);
        });
        services.AddScoped<IOwnerIdentityReader, OwnerIdentityReader>();
        services.AddScoped<IStorageReservationRepository, StorageReservationRepository>();

        // Bàn giao: Executor
        services.AddScoped<IExecutorReader, ExecutorReader>();
        services.AddScoped<IExecutorInvitationRepository, ExecutorInvitationRepository>();
        services.AddScoped<IExecutorInvitationSender, ExecutorInvitationSender>();
        services.AddScoped<IExecutorReminderSender, ExecutorReminderSender>();
        services.AddHostedService<ExecutorInvitationMonitor>();
        services.AddScoped<IHandoverProgressReader, HandoverProgressReader>();

        // Lưu trữ và mã hóa nội dung tài sản
        services.AddOptions<AssetStorageSettings>()
            .Bind(configuration.GetSection("AssetStorage"))
            .Validate(settings => settings.IsR2() || !string.IsNullOrWhiteSpace(settings.RootPath),
                "Thiếu AssetStorage:RootPath.")
            .Validate(settings => !settings.IsR2() || settings.R2.FindError() == null,
                "Thiếu AssetStorage:R2 (cần AccountId, BucketName, AccessKeyId, SecretAccessKey).")
            .ValidateOnStart();

        // KEK cho mã hóa nội dung tài sản. Sai/thiếu khóa thì API không khởi động, tránh lưu nội dung không mở lại được.
        services.AddOptions<AssetEncryptionSettings>()
            .Bind(configuration.GetSection("AssetEncryption"))
            .Validate(settings => DataKeyProtector.FindConfigurationError(settings) == null,
                "Cấu hình AssetEncryption không hợp lệ (cần ActiveKeyId và Keys là base64 32 byte). "
                + "Khi chạy local, chọn profile http (Development).")
            .ValidateOnStart();
        services.AddSingleton<DataKeyProtector>();
        services.AddSingleton<IR2ObjectClient, S3R2ObjectClient>();
        services.AddSingleton<IAssetContentStore>(provider => ChooseAssetContentStore(provider));
        // Mã hóa dữ liệu cá nhân của người nhận, dùng chung DataKeyProtector (DATA-02).
        services.AddSingleton<IBeneficiaryDataProtector, BeneficiaryDataProtector>();

        // Security & Notifications
        services.AddSingleton<IPasswordHasher, Argon2PasswordHasher>();
        services.AddSingleton<ITokenService, JwtTokenService>();
        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUser, CurrentUser>();
        services.AddSingleton<IEmailSender, DevEmailSender>();
        services.AddSingleton<IAppUrlProvider, AppUrlProvider>();

        // Job nền điểm danh: quét DB theo chu kỳ, gửi nhắc qua ICheckInReminderSender (bản dev chỉ ghi log).
        services.Configure<CheckInJobSettings>(configuration.GetSection(CheckInJobSettings.SectionName));
        // Dịch vụ email: Gmail qua MailKit khi có cấu hình Email:Smtp, không thì chỉ ghi log (xem EmailConfig).
        services.AddEmailServices(configuration);
        LegacyVault.Infrastructure.Handover.HandoverConfig.AddHandover(services, configuration);
        LegacyVault.Infrastructure.DeathCases.DeathCaseConfig.AddDeathCases(services, configuration);
        services.AddHostedService<CheckInMonitor>();

        // JWT Bearer Authentication
        services.AddOptions<JwtSettings>()
            .Bind(configuration.GetSection(JwtSettings.SectionName))
            .Validate(settings => !string.IsNullOrWhiteSpace(settings.SigningKey) && Encoding.UTF8.GetByteCount(settings.SigningKey) >= 32,
                "Cấu hình Jwt:SigningKey phải có độ dài tối thiểu 32 bytes.")
            .Validate(settings => !string.IsNullOrWhiteSpace(settings.Issuer),
                "Cấu hình Jwt:Issuer không được để trống.")
            .Validate(settings => !string.IsNullOrWhiteSpace(settings.Audience),
                "Cấu hình Jwt:Audience không được để trống.")
            .Validate(settings => settings.AccessTokenMinutes > 0,
                "Jwt:AccessTokenMinutes phải dương.")
            .ValidateOnStart();

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                var jwtSection = configuration.GetSection(JwtSettings.SectionName);
                var signingKey = jwtSection["SigningKey"];
                if (string.IsNullOrWhiteSpace(signingKey) || Encoding.UTF8.GetByteCount(signingKey) < 32)
                {
                    throw new InvalidOperationException("Cấu hình Jwt:SigningKey thiếu hoặc ngắn hơn 32 bytes.");
                }

                var issuer = jwtSection["Issuer"];
                if (string.IsNullOrWhiteSpace(issuer))
                {
                    throw new InvalidOperationException("Thiếu cấu hình Jwt:Issuer.");
                }

                var audience = jwtSection["Audience"];
                if (string.IsNullOrWhiteSpace(audience))
                {
                    throw new InvalidOperationException("Thiếu cấu hình Jwt:Audience.");
                }

                options.MapInboundClaims = false;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = issuer,
                    ValidateAudience = true,
                    ValidAudience = audience,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = JwtTokenService.CreateSigningKey(signingKey),
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.FromSeconds(30),
                    NameClaimType = JwtRegisteredClaimNames.Sub,
                    RoleClaimType = "role",
                };
            });

        services.AddAuthorization();

        // Đăng nhập Google: Client ID đọc từ cấu hình GoogleAuth:ClientId, không viết cứng trong code.
        services.AddOptions<GoogleAuthSettings>().Bind(configuration.GetSection(GoogleAuthSettings.SectionName));
        services.AddSingleton<IGoogleTokenVerifier, GoogleTokenVerifier>();
        services.AddScoped<IExternalLoginRepository, ExternalLoginRepository>();
        services.AddScoped<IExternalLoginLinkTicketRepository, ExternalLoginLinkTicketRepository>();

        // eKYC: gọi dịch vụ Python riêng qua HTTP. Chưa cấu hình Ekyc:BaseUrl thì mọi lệnh eKYC trả 503.
        services.Configure<EkycSettings>(configuration.GetSection(EkycSettings.SectionName));
        services.AddScoped<IEkycVerificationRepository, EkycVerificationRepository>();
        services.AddScoped<IEkycConsentRepository, EkycConsentRepository>();
        // Mẫu khuôn mặt Owner (EKYC-22, PRIV-03): vector mã hóa envelope, dùng chung DataKeyProtector của tài sản.
        services.AddScoped<IOwnerFaceTemplateRepository, OwnerFaceTemplateRepository>();
        services.AddSingleton<IFaceTemplateProtector, FaceTemplateProtector>();
        // Danh tính đã gắn của Owner (EKYC-27, DATA-02): CCCD đủ 12 số, họ tên, ngày sinh mã hóa envelope.
        services.AddScoped<IOwnerIdentityRepository, OwnerIdentityRepository>();
        services.AddSingleton<IOwnerIdentityProtector, OwnerIdentityProtector>();
        services.AddHttpClient<IEkycClient, EkycHttpClient>((provider, client) =>
        {
            EkycSettings settings = provider.GetRequiredService<IOptions<EkycSettings>>().Value;
            if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
            {
                // Dấu "/" cuối để địa chỉ tương đối "api/ocr" nối đúng vào BaseUrl có đường dẫn.
                client.BaseAddress = new Uri(settings.BaseUrl.TrimEnd('/') + "/");
            }
            if (!string.IsNullOrWhiteSpace(settings.ApiKey))
            {
                client.DefaultRequestHeaders.Add("X-Ekyc-Key", settings.ApiKey);
            }
            client.Timeout = TimeSpan.FromSeconds(settings.TimeoutSeconds);
        });

        return services;
    }

    /// <summary>Chọn nơi lưu nội dung tài sản theo AssetStorage:Provider: "R2" dùng Cloudflare R2, còn lại dùng đĩa server.</summary>
    private static IAssetContentStore ChooseAssetContentStore(IServiceProvider provider)
    {
        AssetStorageSettings settings = provider.GetRequiredService<IOptions<AssetStorageSettings>>().Value;
        if (settings.IsR2())
        {
            return ActivatorUtilities.CreateInstance<R2AssetContentStore>(provider);
        }
        return ActivatorUtilities.CreateInstance<LocalAssetContentStore>(provider);
    }
}
