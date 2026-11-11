using LegacyVault.Application.Notifications;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace LegacyVault.Infrastructure.Notifications;

/// <summary>Gửi thư bổ sung thật qua SMTP, nút dẫn tới trang điểm danh của Chủ kho.</summary>
public sealed class SmtpExtraMailSender : IExtraMailSender
{
    private const string DefaultClientUrl = "http://localhost:5173";

    private readonly SmtpMailSender mailSender;
    private readonly string checkInUrl;

    public SmtpExtraMailSender(SmtpMailSender mailSender, IConfiguration configuration)
    {
        this.mailSender = mailSender;
        string? clientUrl = configuration["App:ClientUrl"];
        if (string.IsNullOrWhiteSpace(clientUrl))
        {
            clientUrl = DefaultClientUrl;
        }
        checkInUrl = clientUrl.TrimEnd('/') + "/dms";
    }

    public Task SendAsync(ExtraMailMessage message, CancellationToken ct)
    {
        string text = $"Xin chào {message.FullName},\n\n{message.Body}\n\n{message.ButtonText}: {checkInUrl}\n\n{message.Note}\n";
        string html = LegacyVaultEmailTheme.BuildHtml(
            message.Title,
            message.FullName,
            message.Body,
            checkInUrl,
            message.ButtonText,
            message.Note,
            "Nhắc điểm danh");
        return mailSender.SendAsync(message.ToEmail, message.FullName, message.Subject, text, html, ct);
    }
}

/// <summary>Bản dev khi chưa cấu hình SMTP: chỉ ghi log, không ghi email đầy đủ (OPS-04).</summary>
public sealed class LogExtraMailSender : IExtraMailSender
{
    private readonly ILogger<LogExtraMailSender> logger;

    public LogExtraMailSender(ILogger<LogExtraMailSender> logger)
    {
        this.logger = logger;
    }

    public Task SendAsync(ExtraMailMessage message, CancellationToken ct)
    {
        logger.LogInformation("[THƯ BỔ SUNG] Tiêu đề: {Subject}", message.Subject);
        return Task.CompletedTask;
    }
}
