using LegacyVault.Application.Notifications;
using LegacyVault.Infrastructure.Notifications;
using Microsoft.Extensions.Configuration;

namespace LegacyVault.Infrastructure.DeathCases;

/// <summary>
/// Gửi email nhắc Verifier thật qua Gmail (SmtpMailSender). SMTP nhận thư = đã nhắc (D3);
/// gửi lỗi thì ném exception để DeathCaseAssignmentJob thử lại ở vòng sau.
/// </summary>
public sealed class SmtpDeathCaseVerifierReminderSender : IDeathCaseVerifierReminderSender
{
    private const string DefaultClientUrl = "http://localhost:5173";

    private readonly SmtpMailSender mailSender;
    private readonly string loginUrl;

    public SmtpDeathCaseVerifierReminderSender(SmtpMailSender mailSender, IConfiguration configuration)
    {
        this.mailSender = mailSender;

        string? clientUrl = configuration["App:ClientUrl"];
        if (string.IsNullOrWhiteSpace(clientUrl))
        {
            clientUrl = DefaultClientUrl;
        }
        loginUrl = clientUrl.TrimEnd('/') + "/login";
    }

    public Task SendAsync(DeathCaseVerifierReminder reminder, CancellationToken ct)
    {
        EmailContent content = DeathCaseVerifierReminderEmail.Build(
            reminder.FullName, reminder.CaseCode, reminder.DueAt, loginUrl);
        return mailSender.SendAsync(reminder.ToEmail, reminder.FullName, content.Subject, content.TextBody, content.HtmlBody, ct);
    }
}
