using System.Globalization;
using System.Net;
using LegacyVault.Application.Notifications;
using LegacyVault.Application.Services;
using LegacyVault.Infrastructure.Notifications;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LegacyVault.Infrastructure.Handover;

/// <summary>Cấu hình mục "Retention" trong appsettings: job lưu và xóa dữ liệu sau bàn giao.</summary>
public sealed class RetentionJobSettings
{
    public const string SectionName = "Retention";

    /// <summary>false thì job không chạy (test tắt để tự gọi IRetentionJob).</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Số giây giữa hai lần quét. Mặc định 300.</summary>
    public int PollSeconds { get; set; } = 300;
}

/// <summary>Dựng thư mốc lưu dữ liệu. OPS-04: không có tên gói hay tài sản.</summary>
public static class RetentionNoticeEmail
{
    private static readonly TimeSpan VietnamOffset = TimeSpan.FromHours(7);

    public static EmailContent Build(RetentionNotice notice, string link)
    {
        string deadline = notice.DeleteAt.ToOffset(VietnamOffset).ToString("HH:mm 'ngày' dd/MM/yyyy", CultureInfo.InvariantCulture);
        string subject = "LegacyVault: dữ liệu bàn giao cho bạn sắp được xóa";
        if (notice.Stage == RetentionRules.NoticeStarted)
        {
            subject = "LegacyVault: bắt đầu thời gian lưu dữ liệu bàn giao cho bạn";
        }
        string paragraph = $"Phần di sản số được chỉ định cho bạn trên LegacyVault sẽ bị xóa vĩnh viễn lúc {deadline} "
            + "nếu bạn chưa hoàn tất việc nhận. Hãy đăng nhập và làm theo hướng dẫn trước thời điểm này.";
        const string note = "Email này không chứa nội dung tài sản. Nếu bạn không biết về việc này, hãy bỏ qua.";

        string text = $"Xin chào {notice.FullName},\n\n{paragraph}\n\nĐăng nhập: {link}\n\n{note}\n";
        string html = LegacyVaultEmailTheme.BuildHtml(
            subject,
            notice.FullName,
            paragraph,
            link,
            "Đăng nhập nhận dữ liệu",
            note);
        return new EmailContent(subject, text, html);
    }
}

public sealed class SmtpRetentionNoticeSender : IRetentionNoticeSender
{
    private const string DefaultClientUrl = "http://localhost:5173";

    private readonly SmtpMailSender mailSender;
    private readonly string loginUrl;

    public SmtpRetentionNoticeSender(SmtpMailSender mailSender, IConfiguration configuration)
    {
        this.mailSender = mailSender;
        string? clientUrl = configuration["App:ClientUrl"];
        if (string.IsNullOrWhiteSpace(clientUrl))
        {
            clientUrl = DefaultClientUrl;
        }
        loginUrl = clientUrl.TrimEnd('/') + "/login";
    }

    public Task SendAsync(RetentionNotice notice, CancellationToken ct)
    {
        EmailContent content = RetentionNoticeEmail.Build(notice, loginUrl);
        return mailSender.SendAsync(notice.ToEmail, notice.FullName, content.Subject, content.TextBody, content.HtmlBody, ct);
    }
}

/// <summary>Chưa cấu hình email: Development chỉ ghi log; môi trường khác ném lỗi, không giả vờ đã gửi.</summary>
public sealed class LogRetentionNoticeSender : IRetentionNoticeSender
{
    private readonly ILogger<LogRetentionNoticeSender> logger;
    private readonly IHostEnvironment environment;

    public LogRetentionNoticeSender(ILogger<LogRetentionNoticeSender> logger, IHostEnvironment environment)
    {
        this.logger = logger;
        this.environment = environment;
    }

    public Task SendAsync(RetentionNotice notice, CancellationToken ct)
    {
        if (!environment.IsDevelopment())
        {
            throw new InvalidOperationException("Chưa cấu hình Email:Smtp nên không gửi được thư mốc lưu dữ liệu.");
        }
        logger.LogInformation("[MỐC LƯU DỮ LIỆU] Lượt {TurnId}, mốc {Stage}; chưa cấu hình email nên không gửi thư.",
            notice.TurnId, notice.Stage);
        return Task.CompletedTask;
    }
}

/// <summary>Vòng lặp nền gọi IRetentionJob theo chu kỳ (cùng mẫu với các monitor khác).</summary>
public sealed class RetentionMonitor : BackgroundService
{
    private readonly IServiceScopeFactory scopeFactory;
    private readonly RetentionJobSettings settings;
    private readonly ILogger<RetentionMonitor> logger;

    public RetentionMonitor(
        IServiceScopeFactory scopeFactory, IOptions<RetentionJobSettings> settings, ILogger<RetentionMonitor> logger)
    {
        this.scopeFactory = scopeFactory;
        this.settings = settings.Value;
        this.logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!settings.Enabled)
        {
            return;
        }
        int pollSeconds = settings.PollSeconds;
        if (pollSeconds < 1)
        {
            pollSeconds = 300;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(pollSeconds), stoppingToken);
                await RunOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogError("Vòng quét lưu dữ liệu lỗi: {ExceptionType}", ex.GetType().Name);
            }
        }
    }

    private async Task RunOnceAsync(CancellationToken ct)
    {
        List<Guid> planIds;
        using (IServiceScope scope = scopeFactory.CreateScope())
        {
            planIds = await scope.ServiceProvider.GetRequiredService<IRetentionJob>().FindPlanIdsAsync(ct);
        }
        foreach (Guid planId in planIds)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                using (IServiceScope scope = scopeFactory.CreateScope())
                {
                    RetentionRunResult result = await scope.ServiceProvider.GetRequiredService<IRetentionJob>()
                        .ProcessPlanAsync(planId, ct);
                    if (result.TurnsDeleted > 0 || result.VaultClosed)
                    {
                        logger.LogInformation("Đã dọn dữ liệu kế hoạch {PlanId}: {Turns} lượt, {Versions} phiên bản, đóng kho {Closed}.",
                            planId, result.TurnsDeleted, result.VersionsShredded, result.VaultClosed);
                    }
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                // Xung đột với thao tác nhận/claim đang chạy (EDIT_CONFLICT) thì vòng sau kiểm lại.
                logger.LogWarning("Xử lý lưu dữ liệu kế hoạch {PlanId} lỗi: {ExceptionType}", planId, ex.GetType().Name);
            }
        }
    }
}
