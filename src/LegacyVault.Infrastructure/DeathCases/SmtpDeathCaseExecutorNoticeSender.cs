using LegacyVault.Application.Notifications;
using LegacyVault.Infrastructure.Notifications;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace LegacyVault.Infrastructure.DeathCases;

/// <summary>
/// Gửi email báo Executor thật qua Gmail (SmtpMailSender). Gửi lỗi thì chỉ ghi log, không ném lỗi,
/// vì quyết định của Verifier đã lưu xong trước khi gửi thư.
/// </summary>
public sealed class SmtpDeathCaseExecutorNoticeSender : IDeathCaseExecutorNoticeSender
{
    private const string DefaultClientUrl = "http://localhost:5173";

    private readonly SmtpMailSender mailSender;
    private readonly ILogger<SmtpDeathCaseExecutorNoticeSender> logger;
    private readonly string executorUrl;

    public SmtpDeathCaseExecutorNoticeSender(
        SmtpMailSender mailSender, IConfiguration configuration, ILogger<SmtpDeathCaseExecutorNoticeSender> logger)
    {
        this.mailSender = mailSender;
        this.logger = logger;

        string? clientUrl = configuration["App:ClientUrl"];
        if (string.IsNullOrWhiteSpace(clientUrl))
        {
            clientUrl = DefaultClientUrl;
        }
        executorUrl = clientUrl.TrimEnd('/') + "/executor/claim";
    }

    public async Task SendAsync(DeathCaseExecutorNotice notice, CancellationToken ct)
    {
        EmailContent content = DeathCaseExecutorNoticeEmail.Build(
            notice.FullName, notice.CaseCode, notice.Kind, notice.SupplementDueAt, executorUrl);
        try
        {
            await mailSender.SendAsync(notice.ToEmail, notice.FullName, content.Subject, content.TextBody, content.HtmlBody, ct);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Không ghi email hay họ tên vào log; chỉ mã hồ sơ và loại lỗi.
            logger.LogWarning("Không gửi được thư báo Executor cho hồ sơ {CaseCode}: {ExceptionType}",
                notice.CaseCode, ex.GetType().Name);
        }
    }
}
