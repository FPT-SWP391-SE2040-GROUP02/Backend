using LegacyVault.Application.Notifications;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace LegacyVault.Infrastructure.Notifications;

/// <summary>Gửi mã tải thật qua SMTP. Thư chỉ có mã và hạn, không có tên gói hay tài sản (OPS-04).</summary>
public sealed class SmtpDownloadCodeSender : IDownloadCodeSender
{
    private readonly SmtpMailSender mailSender;
    private readonly string clientUrl;

    public SmtpDownloadCodeSender(SmtpMailSender mailSender, IConfiguration configuration)
    {
        this.mailSender = mailSender;
        string? configured = configuration["App:ClientUrl"];
        clientUrl = string.IsNullOrWhiteSpace(configured) ? "http://localhost:5173" : configured.TrimEnd('/');
    }

    public Task SendAsync(string toEmail, string fullName, string code, DateTimeOffset expiresAt, CancellationToken ct)
    {
        string subject = "LegacyVault: mã tải của bạn";
        string until = expiresAt.ToOffset(TimeSpan.FromHours(7)).ToString("HH:mm 'ngày' dd/MM/yyyy");
        string paragraph = $"Mã tải của bạn là {code}. Mã dùng một lần và có hiệu lực đến {until}. "
            + "Không chia sẻ mã này với bất kỳ ai, kể cả người tự nhận là nhân viên LegacyVault.";
        string text = $"Xin chào {fullName},\n\n{paragraph}\n";
        string html = LegacyVaultEmailTheme.BuildHtml(
            subject,
            fullName,
            paragraph,
            clientUrl,
            "Mở LegacyVault",
            "Nếu bạn không yêu cầu mã này, hãy bỏ qua email và cân nhắc đổi mật khẩu.",
            "Mã tải tài sản");
        return mailSender.SendAsync(toEmail, fullName, subject, text, html, ct);
    }
}

/// <summary>Chưa cấu hình email: Development chỉ ghi log (không ghi mã); môi trường khác ném lỗi, không giả vờ đã gửi.</summary>
public sealed class LogDownloadCodeSender : IDownloadCodeSender
{
    private readonly ILogger<LogDownloadCodeSender> logger;
    private readonly IHostEnvironment environment;

    public LogDownloadCodeSender(ILogger<LogDownloadCodeSender> logger, IHostEnvironment environment)
    {
        this.logger = logger;
        this.environment = environment;
    }

    public Task SendAsync(string toEmail, string fullName, string code, DateTimeOffset expiresAt, CancellationToken ct)
    {
        if (!environment.IsDevelopment())
        {
            throw new InvalidOperationException("Chưa cấu hình Email:Smtp nên không gửi được mã tải.");
        }
        logger.LogInformation("[MÃ TẢI] Chưa cấu hình email nên không gửi thư; mã hết hạn {ExpiresAt:O}.", expiresAt);
        return Task.CompletedTask;
    }
}
