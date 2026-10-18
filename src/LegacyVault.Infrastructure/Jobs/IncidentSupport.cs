using System.Net;
using LegacyVault.Application.Services;
using LegacyVault.Infrastructure.Notifications;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LegacyVault.Infrastructure.Jobs;

/// <summary>Gửi thư sự cố thật qua Gmail. Chỉ có loại sự cố, mã rút gọn và hạn (SEC-07).</summary>
public sealed class SmtpIncidentNoticeSender : IIncidentNoticeSender
{
    private const string DefaultClientUrl = "http://localhost:5173";

    private readonly SmtpMailSender mailSender;
    private readonly string adminUrl;

    public SmtpIncidentNoticeSender(SmtpMailSender mailSender, IConfiguration configuration)
    {
        this.mailSender = mailSender;
        string? clientUrl = configuration["App:ClientUrl"];
        if (string.IsNullOrWhiteSpace(clientUrl))
        {
            clientUrl = DefaultClientUrl;
        }
        adminUrl = clientUrl.TrimEnd('/') + "/admin";
    }

    public Task SendAsync(IncidentNotice notice, CancellationToken ct)
    {
        string code = notice.IncidentId.ToString("N").Substring(24).ToUpperInvariant();
        string subject = "LegacyVault: sự cố cần xử lý " + code;
        string paragraph = notice.Kind switch
        {
            IncidentNoticeKind.Assigned => $"Bạn được giao sự cố {code} (loại {notice.IncidentType}), hạn xử lý {notice.DueAt:dd/MM/yyyy HH:mm} UTC.",
            IncidentNoticeKind.Reminder => $"Sự cố {code} (loại {notice.IncidentType}) đã tới hạn xử lý {notice.DueAt:dd/MM/yyyy HH:mm} UTC và chưa đóng.",
            _ => $"Sự cố {code} (loại {notice.IncidentType}) quá 72 giờ chưa được xử lý. Vui lòng điều phối.",
        };
        string text = $"Xin chào {notice.FullName},\n\n{paragraph}\n\nMở khu quản trị: {adminUrl}\n";
        string html = $"<p>Xin chào {WebUtility.HtmlEncode(notice.FullName)},</p><p>{WebUtility.HtmlEncode(paragraph)}</p>"
            + $"<p><a href=\"{WebUtility.HtmlEncode(adminUrl)}\">Mở khu quản trị</a></p>";
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
