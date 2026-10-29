using System.Text;
using LegacyVault.Application.Ekyc;
using LegacyVault.Application.Notifications;
using LegacyVault.Application.Repositories;
using LegacyVault.Application.Security;
using LegacyVault.Application.Services;
using LegacyVault.Application.Storage;
using LegacyVault.Infrastructure.Billing;
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
            .PostConfigure(settings =>
            {
                if (settings.Default != null)
                {
                    settings.Default = settings.Default.Trim().Trim('"').Trim('\'');
                }
            })
            .Validate(settings => !string.IsNullOrWhiteSpace(settings.Default),
                "Thiếu ConnectionStrings:Default. Khi chạy local, chọn profile http (Development).")
            .ValidateOnStart();

        services.AddDbContext<AppDbContext>((provider, options) =>
        {
            var raw = provider.GetRequiredService<IOptions<DatabaseSettings>>().Value.Default ?? string.Empty;
            raw = raw.Trim().Trim('"').Trim('\'');
            if (raw.StartsWith("ConnectionStrings__Default=", StringComparison.OrdinalIgnoreCase))
            {
                raw = raw["ConnectionStrings__Default=".Length..].Trim().Trim('"').Trim('\'');
            }
            else if (raw.StartsWith("ConnectionStrings:Default=", StringComparison.OrdinalIgnoreCase))
            {
                raw = raw["ConnectionStrings:Default=".Length..].Trim().Trim('"').Trim('\'');
            }
            options.UseSqlServer(raw);
        });
        services.AddHealthChecks()
            .AddCheck<DatabaseHealthCheck>("database", tags: ["ready"]);

        // IUnitOfWork chính là AppDbContext của request: repository và service dùng chung một instance.
        services.AddScoped<IUnitOfWork>(provider => provider.GetRequiredService<AppDbContext>());

        // Repositories
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<ILoginThrottleRepository, LoginThrottleRepository>();
        services.AddScoped<IEmailVerificationTokenRepository, EmailVerificationTokenRepository>();
        services.AddScoped<IPasswordResetTokenRepository, PasswordResetTokenRepository>();
        // Xác thực hai bước TOTP của Admin: khóa, mã khôi phục, thử thách; bí mật bọc bằng KEK của DataKeyProtector.
        services.AddScoped<ITwoFactorRepository, TwoFactorRepository>();
        services.AddSingleton<ITotpSecretProtector, TotpSecretProtector>();
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

        // Thanh toán (SRS §8): đơn thanh toán và thông tin tài khoản nhận tiền.
        services.AddScoped<IPaymentOrderRepository, PaymentOrderRepository>();
        services.Configure<BillingSettings>(configuration.GetSection(BillingSettings.SectionName));
        services.AddSingleton<IBillingBankSettings, BillingBankSettings>();
        services.AddSingleton<IBillingDevSettings, BillingDevSettings>();
        // Webhook SePay (SRS §8.5): giao dịch tiền vào và cấu hình nhận webhook.
        services.AddScoped<IPaymentTransactionRepository, PaymentTransactionRepository>();
        services.AddSingleton<IBillingSePaySettings, BillingSePaySettings>();

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
            .PostConfigure(settings =>
            {
                if (settings.BlindIndexKey != null) settings.BlindIndexKey = settings.BlindIndexKey.Trim().Trim('"').Trim('\'');
                if (settings.ActiveKeyId != null) settings.ActiveKeyId = settings.ActiveKeyId.Trim().Trim('"').Trim('\'');
                if (settings.Keys != null)
                {
                    foreach (var key in settings.Keys.Keys.ToList())
                    {
                        settings.Keys[key] = settings.Keys[key]?.Trim().Trim('"').Trim('\'') ?? string.Empty;
                    }
                }
            })
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
        services.AddSingleton<IPasswordHasher, Sha256PasswordHasher>();
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
            .PostConfigure(settings =>
            {
                if (settings.SigningKey != null) settings.SigningKey = settings.SigningKey.Trim().Trim('"').Trim('\'');
                if (settings.Issuer != null) settings.Issuer = settings.Issuer.Trim().Trim('"').Trim('\'');
                if (settings.Audience != null) settings.Audience = settings.Audience.Trim().Trim('"').Trim('\'');
            })
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
                var signingKey = jwtSection["SigningKey"]?.Trim().Trim('"').Trim('\'');
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
        services.AddOptions<EkycSettings>()
            .Bind(configuration.GetSection(EkycSettings.SectionName))
            .PostConfigure(settings =>
            {
                if (settings.BaseUrl != null)
                {
                    settings.BaseUrl = settings.BaseUrl.Trim().Trim('"').Trim('\'');
                }
                if (settings.ApiKey != null)
                {
                    settings.ApiKey = settings.ApiKey.Trim().Trim('"').Trim('\'');
                }
                string? rawTimeout = configuration["Ekyc:TimeoutSeconds"] ?? configuration["Ekyc__TimeoutSeconds"];
                if (!string.IsNullOrWhiteSpace(rawTimeout) && int.TryParse(rawTimeout.Trim().Trim('"').Trim('\''), out int t) && t > 0)
                {
                    settings.TimeoutSeconds = t;
                }
                else if (settings.TimeoutSeconds <= 0)
                {
                    settings.TimeoutSeconds = 120;
                }
            });
        services.AddScoped<IEkycVerificationRepository, EkycVerificationRepository>();
        services.AddScoped<IEkycConsentRepository, EkycConsentRepository>();
        // Ngưỡng so khớp cấu hình được (Ekyc:MatchThreshold, mặc định EkycRules.MatchThreshold).
        // Application không tham chiếu IOptions nên bridge qua IEkycOptions (mẫu IHandoverPolicy).
        services.AddOptions<EkycOptions>().Bind(configuration.GetSection(EkycOptions.SectionName));
        services.AddSingleton<IEkycOptions>(
            provider => provider.GetRequiredService<IOptions<EkycOptions>>().Value);
        // Mẫu khuôn mặt Owner (EKYC-22, PRIV-03): vector mã hóa envelope, dùng chung DataKeyProtector của tài sản.
        services.AddScoped<IOwnerFaceTemplateRepository, OwnerFaceTemplateRepository>();
        services.AddSingleton<IFaceTemplateProtector, FaceTemplateProtector>();
        // Danh tính đã gắn của Owner (EKYC-27, DATA-02): CCCD đủ 12 số, họ tên, ngày sinh mã hóa envelope.
        services.AddScoped<IOwnerIdentityRepository, OwnerIdentityRepository>();
        services.AddSingleton<IOwnerIdentityProtector, OwnerIdentityProtector>();
        services.AddHttpClient<IEkycClient, EkycHttpClient>((provider, client) =>
        {
            EkycSettings settings = provider.GetRequiredService<IOptions<EkycSettings>>().Value;
            string baseUrl = (settings.BaseUrl ?? string.Empty).Trim().Trim('"').Trim('\'');
            if (!string.IsNullOrWhiteSpace(baseUrl))
            {
                // Dấu "/" cuối để địa chỉ tương đối "api/ocr" nối đúng vào BaseUrl có đường dẫn.
                client.BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/");
            }
            string apiKey = (settings.ApiKey ?? string.Empty).Trim().Trim('"').Trim('\'');
            if (!string.IsNullOrWhiteSpace(apiKey))
            {
                client.DefaultRequestHeaders.Add("X-Ekyc-Key", apiKey);
            }
            client.Timeout = TimeSpan.FromSeconds(settings.TimeoutSeconds > 0 ? settings.TimeoutSeconds : 120);
        });

        // Công cụ thử nghiệm của Admin (controller chỉ mở ở Development)
        services.AddScoped<IDevToolsRepository, DevToolsRepository>();

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
