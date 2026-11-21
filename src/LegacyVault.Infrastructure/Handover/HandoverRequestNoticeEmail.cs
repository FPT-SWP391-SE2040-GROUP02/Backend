using LegacyVault.Application.Notifications;
using LegacyVault.Infrastructure.Notifications;

namespace LegacyVault.Infrastructure.Handover;

/// <summary>
/// Dựng thư báo có yêu cầu nhận tài sản cho Chủ kho, Người thực thi hoặc luật sư. Hàm thuần, không gửi gì.
/// Thư không có tên tài sản hay giấy tờ; nút dẫn về trang của từng vai.
/// </summary>
public static class HandoverRequestNoticeEmail
{
    public const string Subject = "Có yêu cầu nhận tài sản trên LegacyVault";

    public static EmailContent Build(HandoverRequestNotice notice, string clientUrl)
    {
        string baseUrl = clientUrl.TrimEnd('/');
        string title = "Có yêu cầu nhận tài sản";
        string lead;
        string actionUrl;
        string buttonText;
        string note;
        if (notice.Audience == HandoverRequestNotice.Owner)
        {
            lead = $"{notice.RequesterName} vừa gửi yêu cầu nhận tài sản trong kho của bạn kèm giấy tờ. "
                + "Kho đã được khóa trong lúc luật sư xét giấy tờ.";
            actionUrl = baseUrl + "/dashboard/wills";
            buttonText = "Xem yêu cầu";
            note = "Nếu bạn vẫn khỏe mạnh và không muốn bàn giao, hãy đăng nhập và bấm Hủy yêu cầu. "
                + "Bạn hủy được cho tới khi biên bản bàn giao đủ chữ ký.";
        }
        else if (notice.Audience == HandoverRequestNotice.Executor)
        {
            lead = $"{notice.RequesterName} vừa gửi yêu cầu nhận tài sản trong kho của {notice.OwnerName}. "
                + "Luật sư đang xét giấy tờ và sẽ lập biên bản bàn giao.";
            actionUrl = baseUrl + "/executor";
            buttonText = "Mở không gian Người thực thi";
            note = "Bạn chưa cần làm gì lúc này. Khi biên bản tới bước ký, bạn sẽ ký và gửi mã nhận cho từng người.";
        }
        else
        {
            lead = $"{notice.RequesterName} vừa gửi yêu cầu nhận tài sản trong kho của {notice.OwnerName}. "
                + "Hồ sơ được giao cho bạn xét giấy tờ và lập biên bản bàn giao.";
            actionUrl = baseUrl + "/notary/queue";
            buttonText = "Xem biên bản";
            note = "Giấy tờ chỉ xem trên hệ thống. Việc xác minh pháp lý làm ở ngoài hệ thống.";
        }
        string text = $"Xin chào {notice.FullName},\n\n{title}\n\n{lead}\n\n{buttonText}: {actionUrl}\n\n{note}\n";
        string html = LegacyVaultEmailTheme.BuildHtml(
            title: title,
            recipientName: notice.FullName,
            leadText: lead,
            actionUrl: actionUrl,
            actionButtonText: buttonText,
            securityNotice: note);
        return new EmailContent(Subject, text, html);
    }
}

/// <summary>Gửi thư báo có yêu cầu nhận tài sản qua SMTP. Gửi lỗi thì ném exception.</summary>
public sealed class SmtpHandoverRequestNoticeSender : IHandoverRequestNoticeSender
{
    private readonly SmtpMailSender mailSender;
    private readonly string clientUrl;

    public SmtpHandoverRequestNoticeSender(SmtpMailSender mailSender, Microsoft.Extensions.Configuration.IConfiguration configuration)
    {
        this.mailSender = mailSender;
        clientUrl = ClientUrlSettings.Read(configuration);
    }

    public Task SendAsync(HandoverRequestNotice notice, CancellationToken ct)
    {
        EmailContent content = HandoverRequestNoticeEmail.Build(notice, clientUrl);
        return mailSender.SendAsync(notice.ToEmail, notice.FullName, content, ct);
    }
}

/// <summary>Bản cho môi trường chưa cấu hình email: chỉ ghi log, không ghi email hay tên.</summary>
public sealed class LogHandoverRequestNoticeSender : IHandoverRequestNoticeSender
{
    private readonly Microsoft.Extensions.Logging.ILogger<LogHandoverRequestNoticeSender> logger;

    public LogHandoverRequestNoticeSender(Microsoft.Extensions.Logging.ILogger<LogHandoverRequestNoticeSender> logger)
    {
        this.logger = logger;
    }

    public Task SendAsync(HandoverRequestNotice notice, CancellationToken ct)
    {
        Microsoft.Extensions.Logging.LoggerExtensions.LogInformation(logger,
            "[THƯ BÁO YÊU CẦU NHẬN] Chưa cấu hình email nên không gửi thư ({Audience}).", notice.Audience);
        return Task.CompletedTask;
    }
}
