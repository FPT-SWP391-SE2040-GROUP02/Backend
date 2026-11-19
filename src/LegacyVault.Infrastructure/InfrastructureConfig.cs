using System.Text;
using LegacyVault.Application.Ekyc;
using LegacyVault.Application.Notifications;
using LegacyVault.Application.Repositories;
using LegacyVault.Application.Security;
using LegacyVault.Application.ServiceConfig;
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
using LegacyVault.Infrastructure.ServiceConfig;
using LegacyVault.Infrastructure.Services;
using LegacyVault.Infrastructure.Storage;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace LegacyVault.Infrastructure;

public static class InfrastructureConfig
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services, IConfiguration configuration)
    {
        AddClocks(services);
        AddDatabase(services, configuration);
        AddRepositoriesAndSecretProtectors(services);
        AddBilling(services, configuration);
        AddExecutorAndHandoverSupport(services);
        AddAssetStorage(services, configuration);
        AddNotificationsAndJobs(services, configuration);
        AddJwtAuthentication(services, configuration);
        AddGoogleLogin(services, configuration);
        AddEkyc(services, configuration);
        AddDevTools(services);

        return services;
    }

    /// <summary>Đồng hồ hệ thống: luôn là giờ thật. Demo chuyển trạng thái bằng Công cụ thử nghiệm (dời hạn của từng kho).</summary>
    private static void AddClocks(IServiceCollection services)
    {
        services.AddSingleton<TimeProvider>(TimeProvider.System);
    }

    /// <summary>Chuỗi kết nối, AppDbContext, kiểm tra sức khỏe và IUnitOfWork.</summary>
    private static void AddDatabase(IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<DatabaseSettings>()
            .Bind(configuration.GetSection("ConnectionStrings"))
            .PostConfigure(settings =>
            {
                if (settings.Default != null)
                {
                    settings.Default = ConfigText.Clean(settings.Default);
                }
            })
            .Validate(settings => !string.IsNullOrWhiteSpace(settings.Default),
                "Thiếu ConnectionStrings:Default. Khi chạy local, chọn profile http (Development).")
            .ValidateOnStart();

        services.AddDbContext<AppDbContext>((provider, options) =>
        {
            string raw = provider.GetRequiredService<IOptions<DatabaseSettings>>().Value.Default ?? string.Empty;
            raw = ConfigText.Clean(raw);
            if (raw.StartsWith("ConnectionStrings__Default=", StringComparison.OrdinalIgnoreCase))
            {
                raw = ConfigText.Clean(raw["ConnectionStrings__Default=".Length..]);
            }
            else if (raw.StartsWith("ConnectionStrings:Default=", StringComparison.OrdinalIgnoreCase))
            {
                raw = ConfigText.Clean(raw["ConnectionStrings:Default=".Length..]);
            }
            options.UseSqlServer(raw);
        });
        services.AddHealthChecks()
            .AddCheck<DatabaseHealthCheck>("database", tags: ["ready"]);

        // IUnitOfWork chính là AppDbContext của request: repository và service dùng chung một instance.
        services.AddScoped<IUnitOfWork>(provider => provider.GetRequiredService<AppDbContext>());
    }

    /// <summary>Danh mục tỉnh phường, repository của tài khoản và kho, bộ bọc khóa bí mật, cấu hình dịch vụ ngoài.</summary>
    private static void AddRepositoriesAndSecretProtectors(IServiceCollection services)
    {
        // Danh mục tỉnh/phường: đọc tệp nhúng một lần, dùng chung cho cả ứng dụng (không có trạng thái thay đổi).
        services.AddSingleton<IAdministrativeUnitCatalog, LegacyVault.Infrastructure.Reference.AdministrativeUnitCatalog>();

        // Repositories
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<ILoginThrottleRepository, LoginThrottleRepository>();
        services.AddScoped<IEmailVerificationTokenRepository, EmailVerificationTokenRepository>();
        services.AddScoped<IPasswordResetTokenRepository, PasswordResetTokenRepository>();
        // Xác thực hai bước TOTP của Admin: khóa, mã khôi phục, thử thách; bí mật bọc bằng KEK của DataKeyProtector.
        services.AddScoped<ITwoFactorRepository, TwoFactorRepository>();
        services.AddSingleton<ITotpSecretProtector, TotpSecretProtector>();
        // Khóa API/mật khẩu của cấu hình dịch vụ ngoài do Admin sửa trên web, cũng bọc bằng KEK của DataKeyProtector.
        services.AddSingleton<ISystemConfigSecretProtector, SystemConfigSecretProtector>();
        // Mã hóa mảnh khóa Người thực thi để gửi trực tiếp trên hệ thống
        services.AddSingleton<IExecutorKeyProtector, ExecutorKeyProtector>();
        // Cấu hình dịch vụ ngoài đọc lúc chạy: dòng DB đè appsettings, cache 30 giây, theo dõi lần gọi gần nhất.
        services.AddMemoryCache();
        services.AddScoped<ISystemConfigRepository, SystemConfigRepository>();
        services.AddScoped<ISystemConfigResolver, SystemConfigResolver>();
        services.AddSingleton<ISystemConfigDefaults, SystemConfigDefaults>();
        services.AddSingleton<IServiceStatusTracker, ServiceStatusTracker>();
        // Đọc đồng bộ cho các lớp singleton có thuộc tính đồng bộ (tài khoản nhận tiền, khóa webhook SePay).
        services.AddSingleton<ISyncConfigReader, SyncConfigReader>();
        services.AddScoped<IServiceConnectionTester, EkycConnectionTester>();
        services.AddScoped<IServiceConnectionTester, SmtpConnectionTester>();
        services.AddScoped<IServiceConnectionTester, R2ConnectionTester>();
        services.AddScoped<IRefreshTokenRepository, RefreshTokenRepository>();
        services.AddScoped<IVaultRepository, VaultRepository>();
        services.AddScoped<IContainerRepository, ContainerRepository>();
        services.AddScoped<IAssetRepository, AssetRepository>();
        services.AddScoped<IBeneficiaryRepository, BeneficiaryRepository>();
        services.AddScoped<IVaultPinRepository, VaultPinRepository>();
        services.AddScoped<IExecutorKeyShardRepository, ExecutorKeyShardRepository>();
        services.AddScoped<IWillVersionRepository, WillVersionRepository>();
        services.AddScoped<ICheckInRepository, CheckInRepository>();
        services.AddScoped<IEntitlementExpiryRepository, EntitlementExpiryRepository>();
        services.AddScoped<IAuditLogRepository, AuditLogRepository>();
        services.AddScoped<IHoldRepository, HoldRepository>();
        services.AddScoped<IAdminUserRepository, AdminUserRepository>();
        services.AddScoped<IOwnerIdentityReader, OwnerIdentityReader>();
        services.AddScoped<IStorageReservationRepository, StorageReservationRepository>();
    }

    /// <summary>Đơn thanh toán, bảng giá, webhook SePay và mẫu email.</summary>
    private static void AddBilling(IServiceCollection services, IConfiguration configuration)
    {
        // Thanh toán (SRS §8): đơn thanh toán và thông tin tài khoản nhận tiền.
        services.AddScoped<IPaymentOrderRepository, PaymentOrderRepository>();
        services.Configure<BillingSettings>(configuration.GetSection(BillingSettings.SectionName));
        services.AddSingleton<IBillingBankSettings, BillingBankSettings>();
        services.AddSingleton<IBillingPriceSettings, BillingPriceSettings>();
        // Giá Admin chỉnh trong SystemSettings đè lên Billing:Pricing, đọc lại mỗi lần báo giá.
        services.AddScoped<IBillingPriceProvider, BillingPriceProvider>();
        services.AddSingleton<IBillingDevSettings, BillingDevSettings>();
        // Webhook SePay (SRS §8.5): giao dịch tiền vào và cấu hình nhận webhook.
        services.AddScoped<IPaymentTransactionRepository, PaymentTransactionRepository>();
        services.AddScoped<IHandoverTemplateRepository, HandoverTemplateRepository>();
        services.AddScoped<IEmailTemplateRepository, EmailTemplateRepository>();
        services.AddScoped<IExtraMailRepository, ExtraMailRepository>();
        services.AddSingleton<IEmailTemplateDefaults, EmailTemplateDefaults>();
        services.AddHostedService<EmailTemplateRefresher>();
        services.AddSingleton<IBillingSePaySettings, BillingSePaySettings>();
    }

    /// <summary>Đọc dữ liệu Executor và lời mời.</summary>
    private static void AddExecutorAndHandoverSupport(IServiceCollection services)
    {
        // Bàn giao: Executor
        services.AddScoped<IExecutorReader, ExecutorReader>();
        services.AddScoped<IExecutorAssetReader, ExecutorAssetReader>();
        services.AddScoped<IExecutorInvitationRepository, ExecutorInvitationRepository>();
        services.AddScoped<IExecutorInvitationSender, ExecutorInvitationSender>();
        services.AddScoped<IExecutorReminderSender, ExecutorReminderSender>();
        services.AddHostedService<ExecutorInvitationMonitor>();
        services.AddScoped<IHandoverProgressReader, HandoverProgressReader>();
    }

    /// <summary>Nơi lưu tệp, khóa mã hóa tài sản và dữ liệu cá nhân của người nhận.</summary>
    private static void AddAssetStorage(IServiceCollection services, IConfiguration configuration)
    {
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
                if (settings.BlindIndexKey != null)
                {
                    settings.BlindIndexKey = ConfigText.Clean(settings.BlindIndexKey);
                }
                if (settings.ActiveKeyId != null)
                {
                    settings.ActiveKeyId = ConfigText.Clean(settings.ActiveKeyId);
                }
                if (settings.Keys != null)
                {
                    foreach (string key in settings.Keys.Keys.ToList())
                    {
                        settings.Keys[key] = ConfigText.Clean(settings.Keys[key]);
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
        services.AddSingleton<IRecipientDeclaredDataProtector, RecipientDeclaredDataProtector>();
    }

    /// <summary>Mã băm, token, email, job nền, bàn giao và hồ sơ chứng tử.</summary>
    private static void AddNotificationsAndJobs(IServiceCollection services, IConfiguration configuration)
    {
        // Security & Notifications
        services.AddMemoryCache();
        services.AddScoped<ISystemSettingService, SystemSettingService>();
        services.AddHostedService<RuntimeSettingsRefresher>();
        // Số vòng PBKDF2 cấu hình được (Auth:PasswordHashIterations); sàn 100.000 để cấu hình sai không làm yếu hash.
        int passwordHashIterations = Math.Max(
            100_000, configuration.GetValue("Auth:PasswordHashIterations", Pbkdf2PasswordHasher.DefaultIterations));
        services.AddSingleton<IPasswordHasher>(new Pbkdf2PasswordHasher(passwordHashIterations));
        // PIN kho chỉ 6 chữ số nên PBKDF2 nặng không tăng độ an toàn: giữ nguyên cách băm cũ.
        services.AddSingleton<IVaultPinHasher>(new VaultPinHasher(new Sha256PasswordHasher()));
        services.AddSingleton<ITokenService, JwtTokenService>();
        services.AddScoped<ISessionRevocationValidator, SessionRevocationValidator>();
        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUser, CurrentUser>();
        services.AddSingleton<IEmailSender, DevEmailSender>();
        services.AddSingleton<IAppUrlProvider, AppUrlProvider>();

        // Job nền điểm danh: quét DB theo chu kỳ, gửi nhắc qua ICheckInReminderSender (bản dev chỉ ghi log).
        services.Configure<CheckInJobSettings>(configuration.GetSection(CheckInJobSettings.SectionName));
        // Dịch vụ email: Gmail qua MailKit khi có cấu hình Email:Smtp, không thì chỉ ghi log (xem EmailConfig).
        services.AddEmailServices(configuration);
        services.AddSingleton<IEmailDomainChecker, DnsEmailDomainChecker>();
        services.AddScoped<IEmailBounceStore, EmailBounceStore>();
        services.AddHostedService<BounceMailboxMonitor>();
        LegacyVault.Infrastructure.Handover.HandoverConfig.AddHandover(services, configuration);
        LegacyVault.Infrastructure.DeathCases.DeathCaseConfig.AddDeathCases(services, configuration);
        services.AddHostedService<CheckInMonitor>();
        // Job nền hết hạn dịch vụ (OPLAN-01): kho trả phí hết kỳ thì sang ân hạn chỉ đọc.
        services.Configure<EntitlementExpiryJobSettings>(configuration.GetSection(EntitlementExpiryJobSettings.SectionName));
        services.AddHostedService<EntitlementExpiryMonitor>();
    }

    /// <summary>Xác thực JWT và kiểm tra thu hồi phiên.</summary>
    private static void AddJwtAuthentication(IServiceCollection services, IConfiguration configuration)
    {
        // JWT Bearer Authentication
        services.AddOptions<JwtSettings>()
            .Bind(configuration.GetSection(JwtSettings.SectionName))
            .PostConfigure(settings =>
            {
                if (settings.SigningKey != null)
                {
                    settings.SigningKey = ConfigText.Clean(settings.SigningKey);
                }
                if (settings.Issuer != null)
                {
                    settings.Issuer = ConfigText.Clean(settings.Issuer);
                }
                if (settings.Audience != null)
                {
                    settings.Audience = ConfigText.Clean(settings.Audience);
                }
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
                IConfigurationSection jwtSection = configuration.GetSection(JwtSettings.SectionName);
                string signingKey = ConfigText.Clean(jwtSection["SigningKey"]);
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

                options.Events = new JwtBearerEvents
                {
                    OnTokenValidated = async context =>
                    {
                        var principal = context.Principal;
                        if (principal == null)
                        {
                            return;
                        }

                        var familyIdClaim = principal.FindFirst(JwtTokenService.FamilyIdClaim)?.Value;
                        if (string.IsNullOrWhiteSpace(familyIdClaim) || !Guid.TryParse(familyIdClaim, out var familyId))
                        {
                            return;
                        }

                        var sessionValidator = context.HttpContext.RequestServices.GetService<ISessionRevocationValidator>();
                        if (sessionValidator != null)
                        {
                            var isActive = await sessionValidator.IsSessionActiveAsync(familyId, context.HttpContext.RequestAborted);
                            if (!isActive)
                            {
                                context.Fail("Session has been revoked.");
                            }
                        }
                    }
                };
            });

        // Kiểm hạn token theo cùng đồng hồ với lúc tạo token.
        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IServiceProvider>((options, provider) =>
            {
                TimeProvider timeProvider = provider.GetRequiredService<TimeProvider>();
                options.TokenValidationParameters.LifetimeValidator =
                    (notBefore, expires, token, parameters) => ValidateTokenLifetime(timeProvider, notBefore, expires, parameters);
            });

        services.AddAuthorization();
    }

    /// <summary>Bộ kiểm token Google.</summary>
    private static void AddGoogleLogin(IServiceCollection services, IConfiguration configuration)
    {
        // Đăng nhập Google: Client ID đọc từ cấu hình GoogleAuth:ClientId, không viết cứng trong code.
        services.AddOptions<GoogleAuthSettings>().Bind(configuration.GetSection(GoogleAuthSettings.SectionName));
        services.AddSingleton<IGoogleNonceStore, GoogleNonceStore>();
        services.AddSingleton<IGoogleTokenVerifier, GoogleTokenVerifier>();
        services.AddScoped<IExternalLoginRepository, ExternalLoginRepository>();
        services.AddScoped<IExternalLoginLinkTicketRepository, ExternalLoginLinkTicketRepository>();
    }

    /// <summary>Gọi dịch vụ eKYC và danh tính Owner.</summary>
    private static void AddEkyc(IServiceCollection services, IConfiguration configuration)
    {
        // eKYC: gọi dịch vụ Python riêng qua HTTP. Chưa cấu hình Ekyc:BaseUrl thì mọi lệnh eKYC trả 503.
        services.AddOptions<EkycSettings>()
            .Bind(configuration.GetSection(EkycSettings.SectionName))
            .PostConfigure(settings =>
            {
                if (settings.BaseUrl != null)
                {
                    settings.BaseUrl = ConfigText.Clean(settings.BaseUrl);
                }
                if (settings.ApiKey != null)
                {
                    settings.ApiKey = ConfigText.Clean(settings.ApiKey);
                }
                string? rawTimeout = configuration["Ekyc:TimeoutSeconds"] ?? configuration["Ekyc__TimeoutSeconds"];
                if (!string.IsNullOrWhiteSpace(rawTimeout) && int.TryParse(ConfigText.Clean(rawTimeout), out int t) && t > 0)
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
        services.AddOptions<EkycOptions>()
            .Bind(configuration.GetSection(EkycOptions.SectionName))
            .PostConfigure<IHostEnvironment>((options, environment) =>
            {
                // Tạm ngừng eKYC khi khởi động chỉ dành cho môi trường thử; môi trường thật luôn dùng eKYC thật.
                if (!environment.IsDevelopment() && !environment.IsEnvironment("Staging"))
                {
                    options.PausedOnStartup = false;
                }
            });
        services.AddSingleton<IEkycOptions>(
            provider => provider.GetRequiredService<IOptions<EkycOptions>>().Value);
        // Danh tính đã gắn của Owner (EKYC-27, DATA-02): CCCD đủ 12 số, họ tên, ngày sinh mã hóa envelope.
        services.AddScoped<IOwnerIdentityRepository, OwnerIdentityRepository>();
        // Khóa chỉ mục mù CCCD tách khỏi KEK; để trống thì tắt (chỉ còn kiểm trùng ở tầng ứng dụng).
        services.AddSingleton<ICitizenIdBlindIndex>(new CitizenIdBlindIndex(configuration["Ekyc:CitizenIdIndexKey"]));
        services.AddSingleton<IOwnerIdentityProtector, OwnerIdentityProtector>();
        services.AddHttpClient<IEkycClient, EkycHttpClient>(client =>
        {
            // Địa chỉ, khóa và thời gian chờ đọc theo từng lời gọi từ cấu hình lúc chạy (Admin sửa trên web);
            // thời gian chờ do EkycHttpClient tự canh nên HttpClient không đặt giới hạn riêng.
            client.Timeout = Timeout.InfiniteTimeSpan;
        });
    }

    /// <summary>Công cụ thử nghiệm của Admin.</summary>
    private static void AddDevTools(IServiceCollection services)
    {
        // Công cụ thử nghiệm của Admin (controller chỉ mở ở Development)
        services.AddScoped<IDevToolsRepository, DevToolsRepository>();
    }

    /// <summary>Chọn nơi lưu nội dung tài sản theo AssetStorage:Provider: "R2" dùng Cloudflare R2, còn lại dùng đĩa server.</summary>
    /// <summary>
    /// Kiểm hạn token theo TimeProvider thay vì giờ thật của máy (thư viện chưa cho gắn TimeProvider trực tiếp).
    /// Cùng luật mặc định: phải có hạn, chưa tới giờ bắt đầu hoặc đã quá hạn (cộng ClockSkew) thì từ chối.
    /// </summary>
    private static bool ValidateTokenLifetime(
        TimeProvider timeProvider, DateTime? notBefore, DateTime? expires, TokenValidationParameters parameters)
    {
        DateTime now = timeProvider.GetUtcNow().UtcDateTime;
        if (expires == null)
        {
            throw new SecurityTokenNoExpirationException("Token không có thời hạn.");
        }
        if (notBefore != null && notBefore.Value > now + parameters.ClockSkew)
        {
            SecurityTokenNotYetValidException notYetValid = new SecurityTokenNotYetValidException("Token chưa có hiệu lực.");
            notYetValid.NotBefore = notBefore.Value;
            throw notYetValid;
        }
        if (expires.Value < now - parameters.ClockSkew)
        {
            SecurityTokenExpiredException expired = new SecurityTokenExpiredException("Token đã hết hạn.");
            expired.Expires = expires.Value;
            throw expired;
        }
        return true;
    }

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
