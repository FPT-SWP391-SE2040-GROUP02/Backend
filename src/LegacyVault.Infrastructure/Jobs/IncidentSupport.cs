using System.Net;
using LegacyVault.Application.Services;
using LegacyVault.Infrastructure.Notifications;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LegacyVault.Infrastructure.Jobs;

/// <summary>
/// Gửi thư sự cố thật qua Gmail. Chỉ có loại sự cố, mã rút gọn và hạn (SEC-07). Link theo khu vực người nhận:
/// Executor về trang Executor của kho, Verifier về trang Verifier, Admin về danh sách sự cố.
/// </summary>
public sealed class SmtpIncidentNoticeSender : IIncidentNoticeSender
{
    private const string DefaultClientUrl = "http://localhost:5173";

    private readonly SmtpMailSender mailSender;
    private readonly string clientUrl;

    public SmtpIncidentNoticeSender(SmtpMailSender mailSender, IConfiguration configuration)
    {
        this.mailSender = mailSender;
        string? configured = configuration["App:ClientUrl"];
        if (string.IsNullOrWhiteSpace(configured))
        {
            configured = DefaultClientUrl;
        }
        clientUrl = configured.TrimEnd('/');
    }

    public Task SendAsync(IncidentNotice notice, CancellationToken ct)
    {
        string code = notice.IncidentId.ToString("N").Substring(24).ToUpperInvariant();
        if (notice.Area == IncidentRecipientArea.Executor && notice.IncidentType == "CHECK_IN_OVERDUE")
        {
            // DMS-05: Executor không cần biết khái niệm "sự cố"; dùng mẫu ExecutorOverdueNoticeEmail chuẩn LegacyVaultEmailTheme.
            string executorLink = clientUrl + "/executor";
            if (notice.VaultId != null)
            {
                executorLink = executorLink + "?vault=" + notice.VaultId.Value.ToString();
            }
            EmailContent overdueEmail = ExecutorOverdueNoticeEmail.Build(
                notice.FullName, executorLink, notice.Kind == IncidentNoticeKind.Reminder);
            return mailSender.SendAsync(notice.ToEmail, notice.FullName, overdueEmail, ct);
        }

        string subject = "LegacyVault: sự cố cần xử lý " + code;
        string paragraph;
        if (notice.Kind == IncidentNoticeKind.Assigned)
        {
            paragraph = $"Bạn được giao sự cố {code} (loại {notice.IncidentType}), hạn xử lý {notice.DueAt:dd/MM/yyyy HH:mm} UTC.";
        }
        else if (notice.Kind == IncidentNoticeKind.Reminder)
        {
            paragraph = $"Sự cố {code} (loại {notice.IncidentType}) đã tới hạn xử lý {notice.DueAt:dd/MM/yyyy HH:mm} UTC và chưa đóng.";
        }
        else
        {
            paragraph = $"Sự cố {code} (loại {notice.IncidentType}) quá 72 giờ chưa được xử lý. Vui lòng điều phối.";
        }

        string linkText;
        string link;
        if (notice.Area == IncidentRecipientArea.Verifier)
        {
            linkText = "Mở trang người xét duyệt";
            link = clientUrl + "/notary/workspace";
        }
        else
        {
            linkText = "Mở danh sách sự cố";
            link = clientUrl + "/admin?view=incidents";
        }

        string text = $"Xin chào {notice.FullName},\n\n{paragraph}\n\n{linkText}: {link}\n";
        string html = LegacyVaultEmailTheme.BuildHtml(
            subject,
            notice.FullName,
            paragraph,
            link,
            linkText,
            "Email này dành cho nhân sự được phân công xử lý sự cố hệ thống.",
            "Thông báo sự cố");
        return mailSender.SendAsync(notice.ToEmail, notice.FullName, subject, text, html, ct);
    }
}

/// <summary>Chưa cấu hình email: Development chỉ ghi log; môi trường khác ném lỗi, không giả vờ đã gửi.</summary>
public sealed class LogIncidentNoticeSender : IIncidentNoticeSender
{
    private readonly ILogger<LogIncidentNoticeSender> logger;
    private readonly IHostEnvironment environment;

    public LogIncidentNoticeSender(ILogger<LogIncidentNoticeSender> logger, IHostEnvironment environment)
    {
        this.logger = logger;
        this.environment = environment;
    }

    public Task SendAsync(IncidentNotice notice, CancellationToken ct)
    {
        if (!environment.IsDevelopment())
        {
            throw new InvalidOperationException("Chưa cấu hình Email:Smtp nên không gửi được thư sự cố.");
        }
        logger.LogInformation("[SỰ CỐ] {Kind} sự cố {IncidentId} loại {Type}; chưa cấu hình email nên không gửi thư.",
            notice.Kind, notice.IncidentId, notice.IncidentType);
        return Task.CompletedTask;
    }
}

/// <summary>Vòng lặp nền gọi IIncidentJob. Cấu hình: Incident:JobEnabled (mặc định true), Incident:PollSeconds (mặc định 300).</summary>
public sealed class IncidentMonitor(IServiceScopeFactory scopeFactory, IConfiguration config,
    ILogger<IncidentMonitor> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!config.GetValue("Incident:JobEnabled", true))
        {
            return;
        }
        int pollSeconds = config.GetValue("Incident:PollSeconds", 300);
        if (pollSeconds < 1)
        {
            pollSeconds = 300;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(pollSeconds), stoppingToken);
                List<Guid> ids;
                using (IServiceScope scope = scopeFactory.CreateScope())
                {
                    ids = await scope.ServiceProvider.GetRequiredService<IIncidentJob>().FindIdsAsync(stoppingToken);
                }
                // Quét mở sự cố mới (hồ sơ quá hạn xét, bổ sung nhiều lần, gói sắp hết) trước khi xử lý các sự cố đang mở.
                try
                {
                    using (IServiceScope scope = scopeFactory.CreateScope())
                    {
                        await scope.ServiceProvider.GetRequiredService<IOperationsScanner>().ScanAsync(stoppingToken);
                    }
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    logger.LogWarning("Quét sự cố vận hành lỗi: {ExceptionType}", ex.GetType().Name);
                }
                foreach (Guid id in ids)
                {
                    try
                    {
                        using (IServiceScope scope = scopeFactory.CreateScope())
                        {
                            await scope.ServiceProvider.GetRequiredService<IIncidentJob>().ProcessAsync(id, stoppingToken);
                        }
                    }
                    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        logger.LogWarning("Xử lý sự cố {IncidentId} lỗi: {ExceptionType}", id, ex.GetType().Name);
                    }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogError("Vòng quét sự cố lỗi: {ExceptionType}", ex.GetType().Name);
            }
        }
    }
}
