namespace LegacyVault.Infrastructure.Notifications;

/// <summary>Nội dung một email: tiêu đề cùng bản chữ thuần và bản HTML của thân thư.</summary>
public sealed class EmailContent
{
    public EmailContent(string subject, string textBody, string htmlBody)
    {
        Subject = subject;
        TextBody = textBody;
        HtmlBody = htmlBody;
    }

    public string Subject { get; }

    public string TextBody { get; }

    public string HtmlBody { get; }
}
