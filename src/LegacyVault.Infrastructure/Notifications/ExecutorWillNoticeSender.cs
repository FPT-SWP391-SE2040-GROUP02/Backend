using LegacyVault.Application.Notifications;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LegacyVault.Infrastructure.Notifications;

/// <summary>Cùng công tắc với thư mời Người thực thi; chưa bật thì trả false, không giả vờ đã gửi.</summary>
public sealed class ExecutorWillNoticeSender : IExecutorWillNoticeSender
{
    private readonly SmtpMailSender sender;
    private readonly IOptions<SmtpSettings> settings;
    private readonly IConfiguration config;
    private readonly IAppUrlProvider urlProvider;
    private readonly ILogger<ExecutorWillNoticeSender> logger;

    public ExecutorWillNoticeSender(SmtpMailSender sender, IOptions<SmtpSettings> settings,
        IConfiguration config, IAppUrlProvider urlProvider, ILogger<ExecutorWillNoticeSender> logger)
    {
        this.sender = sender;
        this.settings = settings;
        this.config = config;
        this.urlProvider = urlProvider;
        this.logger = logger;
    }

    /// <summary>Nội dung một thư báo: tiêu đề thư, bản văn bản thuần và bản HTML.</summary>
    public sealed record NoticeMail(string Subject, string Text, string Html);

    /// <summary>
    /// Thư nhắc Người thực thi: Chủ tài sản vừa cập nhật biên bản phân chia, cần xem lại và ký nhận vai lại.
    /// Tách riêng để bản xem thử và bản gửi thật dùng chung một nội dung.
    /// </summary>
    public static NoticeMail BuildNewVersionMail(string executorEmail, string ownerName, int versionNo, string link)
    {
        string subject = "Biên bản phân chia có phiên bản mới, nhờ bạn ký lại";
        string lead = "Chủ tài sản " + ownerName + " vừa cập nhật và ký biên bản phân chia, hiện là phiên bản " + versionNo + ". "
            + "Bạn cần xem lại biên bản rồi ký nhận vai lại.";
        string text = lead + " Mở LegacyVault tại " + link + " , vào mục Lời mời, đọc biên bản rồi ký bằng mật khẩu tài khoản "
            + "hoặc chọn không đồng ý.";

        List<LegacyVaultEmailTheme.MailListSection> sections = new List<LegacyVaultEmailTheme.MailListSection>();
        sections.Add(new LegacyVaultEmailTheme.MailListSection("Bạn cần làm:", new string[]
        {
            "Đăng nhập LegacyVault và mở mục Lời mời.",
            "Đọc biên bản phân chia phiên bản " + versionNo + ".",
            "Ký nhận vai lại bằng mật khẩu tài khoản nếu đồng ý, hoặc chọn Không đồng ý.",
        }));

        List<LegacyVaultEmailTheme.MailParagraph> closing = new List<LegacyVaultEmailTheme.MailParagraph>();
        closing.Add(new LegacyVaultEmailTheme.MailParagraph(
            null,
            "Nếu bạn không đồng ý, Chủ tài sản sẽ được báo để sửa lại hoặc mời người khác."));

        string html = LegacyVaultEmailTheme.BuildHtml(
            "Biên bản phân chia có phiên bản mới",
            executorEmail,
            lead,
            link,
            "Mở biên bản và ký lại",
            "Chỉ ký khi bạn đã đọc kỹ biên bản. Nếu bạn không quen Chủ tài sản này, hãy bỏ qua thư.",
            "Nhắc ký lại",
            false,
            null,
            null,
            null,
            null,
            null,
            null,
            sections,
            "Kính gửi anh/chị,",
            closing);
        return new NoticeMail(subject, text, html);
    }

    /// <summary>Thư báo Chủ tài sản: Người thực thi không đồng ý phiên bản biên bản phân chia.</summary>
    public static NoticeMail BuildDeclinedMail(string ownerEmail, string executorName, int versionNo, string link)
    {
        string subject = "Người thực thi không đồng ý biên bản phân chia";
        string lead = "Người thực thi " + executorName + " không đồng ý biên bản phân chia phiên bản " + versionNo + ".";
        string text = lead + " Bạn có thể sửa và ký lại, hoặc mời Người thực thi khác tại " + link + " .";

        List<LegacyVaultEmailTheme.MailListSection> sections = new List<LegacyVaultEmailTheme.MailListSection>();
        sections.Add(new LegacyVaultEmailTheme.MailListSection("Bạn có thể:", new string[]
        {
            "Sửa cách chia rồi ký lại biên bản.",
            "Hoặc mời một Người thực thi khác.",
        }));

        string html = LegacyVaultEmailTheme.BuildHtml(
            "Người thực thi không đồng ý biên bản",
            ownerEmail,
            lead,
            link,
            "Mở trang Bàn giao",
            "Thư tự động từ LegacyVault. Hãy đăng nhập để xử lý, không chuyển tiếp thư này.",
            "Thông báo",
            false,
            null,
            null,
            null,
            null,
            null,
            null,
            sections,
            "Kính gửi anh/chị,");
        return new NoticeMail(subject, text, html);
    }

    public Task<bool> SendNewVersionAsync(string executorEmail, string ownerName, int versionNo, CancellationToken ct)
    {
        NoticeMail mail = BuildNewVersionMail(executorEmail, ownerName, versionNo, urlProvider.GetExecutorInboxUrl());
        return SendAsync(executorEmail, mail, ct);
    }

    public Task<bool> SendDeclinedAsync(string ownerEmail, string executorName, int versionNo, CancellationToken ct)
    {
        NoticeMail mail = BuildDeclinedMail(ownerEmail, executorName, versionNo, urlProvider.GetOwnerHandoverUrl());
        return SendAsync(ownerEmail, mail, ct);
    }

    private async Task<bool> SendAsync(string email, NoticeMail mail, CancellationToken ct)
    {
        bool deliveryEnabled = config.GetValue<bool?>("ExecutorInvitations:DeliveryEnabled") ?? settings.Value.IsConfigured;
        if (!deliveryEnabled || !settings.Value.IsConfigured)
        {
            return false;
        }
        try
        {
            await sender.SendAsync(email, "", mail.Subject, mail.Text, mail.Html, ct);
            return true;
        }
        catch (Exception exception) when (!ct.IsCancellationRequested)
        {
            logger.LogWarning("Executor will notice delivery failed ({ExceptionType}).", exception.GetType().Name);
            return false;
        }
    }
}
