namespace LegacyVault.Infrastructure.Notifications;

/// <summary>Nội dung một email: tiêu đề cùng bản chữ thuần và bản HTML của thân thư.</summary>
public sealed class EmailContent
{
    public EmailContent(string subject, string textBody, string htmlBody, string? templateKey = null, LegacyVault.Application.Notifications.EmailParts? defaults = null)
    {
        Subject = subject;
        TextBody = textBody;
        HtmlBody = htmlBody;
        TemplateKey = templateKey;
        Defaults = defaults;
    }

    public string Subject { get; }

    public string TextBody { get; }

    public string HtmlBody { get; }

    /// <summary>Mã loại thư (EmailTemplateCatalog) để biết Admin có tắt loại này không; null thì luôn gửi.</summary>
    public string? TemplateKey { get; }

    /// <summary>Chữ gốc chưa qua chỉnh của Admin; chỉ có khi dựng để lấy mẫu.</summary>
    public LegacyVault.Application.Notifications.EmailParts? Defaults { get; }
}
