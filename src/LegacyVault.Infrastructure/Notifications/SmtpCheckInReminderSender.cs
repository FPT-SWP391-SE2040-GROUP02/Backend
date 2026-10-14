using LegacyVault.Application.Notifications;
using LegacyVault.Domain.Enums;
using Microsoft.Extensions.Configuration;

namespace LegacyVault.Infrastructure.Notifications;

/// <summary>Gửi email nhắc điểm danh thật qua Gmail (SmtpMailSender). Gửi lỗi thì ném exception để job thử lại.</summary>
public sealed class SmtpCheckInReminderSender : ICheckInReminderSender
{
    private const string DefaultClientUrl = "http://localhost:5173";

    private readonly SmtpMailSender mailSender;
    private readonly string checkInUrl;

    public SmtpCheckInReminderSender(SmtpMailSender mailSender, IConfiguration configuration)
    {
        this.mailSender = mailSender;

        string? clientUrl = configuration["App:ClientUrl"];
        if (string.IsNullOrWhiteSpace(clientUrl))
        {
            clientUrl = DefaultClientUrl;
        }
        checkInUrl = clientUrl.TrimEnd('/') + "/checkin";
    }

    public Task SendReminderAsync(
        string toEmail, string fullName, CheckInReminderKind kind,
        DateTimeOffset dueAt, DateTimeOffset graceEndsAt, CancellationToken ct)
    {
        EmailContent content = CheckInReminderEmail.Build(kind, fullName, dueAt, graceEndsAt, checkInUrl);
        return mailSender.SendAsync(toEmail, fullName, content.Subject, content.TextBody, content.HtmlBody, ct);
    }
}
