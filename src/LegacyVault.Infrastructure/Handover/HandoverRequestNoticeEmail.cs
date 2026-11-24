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

    public const string ReportSubject = "Có hồ sơ báo tin cần xét trên LegacyVault";

    public const string CancelledSubject = "Yêu cầu nhận tài sản đã được hủy trên LegacyVault";

    public const string SigningSubject = "Biên bản bàn giao chờ bạn ký trên LegacyVault";

    public const string LockedSubject = "Kho đã được khóa để bàn giao trên LegacyVault";

    public const string ExecutorChangedSubject = "Đổi Người thực thi của kho đang bàn giao trên LegacyVault";

    public static EmailContent Build(HandoverRequestNotice notice, string clientUrl)
    {
        string baseUrl = clientUrl.TrimEnd('/');
        if (notice.Stage == HandoverRequestNotice.StageSigning)
        {
            return BuildSigning(notice, baseUrl);
        }
        if (notice.Stage == HandoverRequestNotice.StageReport)
        {
            return BuildReport(notice, baseUrl);
        }
        if (notice.Stage == HandoverRequestNotice.StageCancelled)
        {
            return BuildCancelled(notice, baseUrl);
        }
        if (notice.Stage == HandoverRequestNotice.StageLocked)
        {
            return BuildLocked(notice, baseUrl);
        }
        if (notice.Stage == HandoverRequestNotice.StageExecutorChanged)
        {
            return BuildExecutorChanged(notice, baseUrl);
        }
        string title = "Có yêu cầu nhận tài sản";
        string lead;
        string actionUrl;
        string buttonText;
        string note;
        if (notice.Audience == HandoverRequestNotice.Owner)
        {
            lead = $"{notice.RequesterName} vừa gửi yêu cầu nhận tài sản trong kho của bạn kèm giấy tờ. "
                + "Luật sư đang xét giấy tờ; kho chưa bị khóa.";
            actionUrl = baseUrl + "/dashboard/wills";
            buttonText = "Xem yêu cầu";
            note = "Bạn vẫn sửa được kho cho tới khi luật sư chấp nhận giấy tờ. "
                + "Nếu yêu cầu không đúng, hãy liên hệ LegacyVault để được hỗ trợ.";
        }
        else if (notice.Audience == HandoverRequestNotice.Executor)
        {
            lead = $"{notice.RequesterName} vừa gửi yêu cầu nhận tài sản trong kho của {notice.OwnerName}. "
                + "Luật sư đang xét giấy tờ và sẽ lập biên bản bàn giao.";
            actionUrl = baseUrl + "/executor";
            buttonText = "Mở không gian Người thực thi";
            note = "Bạn chưa cần làm gì lúc này. Khi luật sư chốt biên bản, bạn ký và gửi mã nhận cho từng người nhận "
                + "khi người đó ký xong.";
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

    /// <summary>Thư báo Chủ kho và Người thực thi: luật sư đã chấp nhận giấy tờ nên kho đã khóa. Không nêu giấy tờ.</summary>
    private static EmailContent BuildLocked(HandoverRequestNotice notice, string baseUrl)
    {
        string title = "Kho đã được khóa để bàn giao";
        string lead;
        string actionUrl;
        string buttonText;
        string note;
        if (notice.Audience == HandoverRequestNotice.Owner)
        {
            lead = $"Luật sư đã chấp nhận giấy tờ của {notice.RequesterName} và bắt đầu lập biên bản bàn giao. "
                + "Kho của bạn đã được khóa: tài sản, người nhận và cách chia không sửa được nữa.";
            actionUrl = baseUrl + "/dashboard/wills";
            buttonText = "Xem yêu cầu";
            note = "Nếu yêu cầu không đúng, hãy liên hệ LegacyVault để được hỗ trợ.";
        }
        else
        {
            lead = $"Luật sư đã chấp nhận giấy tờ của {notice.RequesterName} và bắt đầu lập biên bản bàn giao kho của "
                + $"{notice.OwnerName}.";
            actionUrl = baseUrl + "/executor";
            buttonText = "Mở không gian Người thực thi";
            note = "Bạn chưa cần làm gì lúc này. Khi luật sư chốt biên bản, bạn ký và gửi mã nhận cho từng người nhận "
                + "khi người đó ký xong.";
        }
        string text = $"Xin chào {notice.FullName},\n\n{title}\n\n{lead}\n\n{buttonText}: {actionUrl}\n\n{note}\n";
        string html = LegacyVaultEmailTheme.BuildHtml(
            title: title,
            recipientName: notice.FullName,
            leadText: lead,
            actionUrl: actionUrl,
            actionButtonText: buttonText,
            securityNotice: note);
        return new EmailContent(LockedSubject, text, html);
    }

    /// <summary>
    /// Thư báo Admin đổi Người thực thi khi đang bàn giao: người mới nhận việc ngay, người cũ thôi vai trò, luật sư biết người
    /// ký và giao từ nay. Không nêu lý do hay tài sản.
    /// </summary>
    private static EmailContent BuildExecutorChanged(HandoverRequestNotice notice, string baseUrl)
    {
        string title = "Đổi Người thực thi của kho đang bàn giao";
        string lead;
        string actionUrl;
        string buttonText;
        string note;
        if (notice.Audience == HandoverRequestNotice.Executor)
        {
            lead = $"Quản trị viên LegacyVault đã giao bạn làm Người thực thi của kho {notice.OwnerName} đang bàn giao. "
                + "Bạn nhận việc ngay, không cần chấp nhận lời mời.";
            actionUrl = baseUrl + "/executor";
            buttonText = "Mở không gian Người thực thi";
            note = "Nếu Người thực thi trước chưa ký biên bản, bạn ký thay. Bạn gửi mã nhận và ký phiếu giao nhận cho từng "
                + "người nhận theo biên bản luật sư đã lập.";
        }
        else if (notice.Audience == HandoverRequestNotice.FormerExecutor)
        {
            lead = $"Quản trị viên LegacyVault đã giao việc bàn giao kho {notice.OwnerName} cho Người thực thi khác. "
                + "Bạn không còn là Người thực thi của kho này.";
            actionUrl = baseUrl + "/executor";
            buttonText = "Mở LegacyVault";
            note = "Chữ ký bạn đã ký trên biên bản vẫn giữ hiệu lực. Từ nay bạn không xem được mã nhận của kho này.";
        }
        else
        {
            lead = $"Quản trị viên LegacyVault đã đổi Người thực thi của kho {notice.OwnerName} mà bạn đang lập biên bản.";
            actionUrl = baseUrl + "/notary/queue";
            buttonText = "Xem biên bản";
            note = "Người thực thi mới ký biên bản (nếu người trước chưa ký), gửi mã nhận và ký phiếu giao nhận từ nay.";
        }
        string text = $"Xin chào {notice.FullName},\n\n{title}\n\n{lead}\n\n{buttonText}: {actionUrl}\n\n{note}\n";
        string html = LegacyVaultEmailTheme.BuildHtml(
            title: title,
            recipientName: notice.FullName,
            leadText: lead,
            actionUrl: actionUrl,
            actionButtonText: buttonText,
            securityNotice: note);
        return new EmailContent(ExecutorChangedSubject, text, html);
    }

    /// <summary>Thư báo Chủ kho và Người thực thi: luật sư đã hủy biên bản đang lập. Không nêu lý do hay giấy tờ.</summary>
    private static EmailContent BuildCancelled(HandoverRequestNotice notice, string baseUrl)
    {
        string title = "Yêu cầu nhận tài sản đã được hủy";
        string lead = $"Luật sư đã hủy biên bản bàn giao đang lập của kho {notice.OwnerName}. Kho đã được mở khóa.";
        string actionUrl = baseUrl + "/dashboard";
        if (notice.Audience == HandoverRequestNotice.Executor)
        {
            actionUrl = baseUrl + "/executor";
        }
        string buttonText = "Mở LegacyVault";
        string note = "Bạn không cần làm gì thêm.";
        string text = $"Xin chào {notice.FullName},\n\n{title}\n\n{lead}\n\n{buttonText}: {actionUrl}\n\n{note}\n";
        string html = LegacyVaultEmailTheme.BuildHtml(
            title: title,
            recipientName: notice.FullName,
            leadText: lead,
            actionUrl: actionUrl,
            actionButtonText: buttonText,
            securityNotice: note);
        return new EmailContent(CancelledSubject, text, html);
    }

    /// <summary>Thư báo luật sư có hồ sơ báo tin chờ xét. Kho chưa bị khóa ở bước này.</summary>
    private static EmailContent BuildReport(HandoverRequestNotice notice, string baseUrl)
    {
        string title = "Có hồ sơ báo tin cần xét";
        string lead = $"{notice.RequesterName} vừa nộp giấy tờ báo tin {notice.OwnerName} đã mất. "
            + "Hồ sơ được giao cho bạn kiểm giấy tờ.";
        string actionUrl = baseUrl + "/notary/queue";
        string buttonText = "Xem hồ sơ";
        string note = "Kho chỉ bị khóa và biên bản chỉ được mở khi bạn chấp nhận hồ sơ. "
            + "Việc xác minh giấy tờ làm ở ngoài hệ thống.";
        string text = $"Xin chào {notice.FullName},\n\n{title}\n\n{lead}\n\n{buttonText}: {actionUrl}\n\n{note}\n";
        string html = LegacyVaultEmailTheme.BuildHtml(
            title: title,
            recipientName: notice.FullName,
            leadText: lead,
            actionUrl: actionUrl,
            actionButtonText: buttonText,
            securityNotice: note);
        return new EmailContent(ReportSubject, text, html);
    }

    /// <summary>
    /// Thư báo ký biên bản: người nhận (khi danh tính của chính mình đã xong) và Người thực thi (khi luật sư vừa chốt).
    /// Mỗi người nhận được giao riêng, không chờ người khác. Không nêu tài sản hay giấy tờ.
    /// </summary>
    private static EmailContent BuildSigning(HandoverRequestNotice notice, string baseUrl)
    {
        string title = "Biên bản bàn giao chờ bạn ký";
        string lead;
        string actionUrl;
        string buttonText;
        string note;
        if (notice.Audience == HandoverRequestNotice.Recipient)
        {
            lead = $"Luật sư đã xác minh danh tính của bạn xong. Bạn có thể đọc và ký biên bản bàn giao tài sản trong kho của "
                + $"{notice.OwnerName} ngay, không cần chờ người nhận khác.";
            actionUrl = baseUrl + "/claim/beneficiary";
            buttonText = "Đọc và ký biên bản";
            note = "Bạn ký xong và luật sư, Người thực thi cũng đã ký thì phần của bạn được mở; Người thực thi gửi mã nhận "
                + "để bạn tải tài sản. Hãy đăng nhập bằng đúng email nhận thư này.";
        }
        else
        {
            lead = $"Luật sư đã chốt biên bản bàn giao tài sản trong kho của {notice.OwnerName}. "
                + "Bạn có thể ký biên bản ngay.";
            actionUrl = baseUrl + "/executor";
            buttonText = "Mở không gian Người thực thi";
            note = "Mỗi người nhận được giao riêng: người nào đã ký (cùng chữ ký của bạn và luật sư) thì bạn gửi mã nhận "
                + "cho người đó, không cần chờ người nhận khác.";
        }
        string text = $"Xin chào {notice.FullName},\n\n{title}\n\n{lead}\n\n{buttonText}: {actionUrl}\n\n{note}\n";
        string html = LegacyVaultEmailTheme.BuildHtml(
            title: title,
            recipientName: notice.FullName,
            leadText: lead,
            actionUrl: actionUrl,
            actionButtonText: buttonText,
            securityNotice: note);
        return new EmailContent(SigningSubject, text, html);
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
