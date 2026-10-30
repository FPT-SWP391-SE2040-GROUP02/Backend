using System.Net;
using System.Text;

namespace LegacyVault.Infrastructure.Notifications;

/// <summary>
/// Mẫu HTML email của LegacyVault theo bản thiết kế canvas v119: đầu thư xanh rừng, nhãn nhỏ phía trên tiêu đề,
/// nút vàng, khung liên kết dự phòng và ghi chú cuối thư.
/// Màu: xanh rừng #1A4B3B / #0A281E, vàng #E0C486, nền thư #EFECE3.
/// Chỉ dùng bảng và style nội tuyến để Gmail, Outlook, Apple Mail hiển thị giống nhau; không dùng SVG, phông web hay JavaScript.
/// </summary>
public static class LegacyVaultEmailTheme
{
    private const string FontStack = "'Be Vietnam Pro','Segoe UI',Roboto,Helvetica,Arial,sans-serif";

    /// <summary>Mã nội tuyến của logo; SmtpMailSender đính ảnh vào thư khi HTML có tham chiếu "cid:" này.</summary>
    public const string LogoContentId = "legacyvault-logo";

    /// <summary>Tên tài nguyên nhúng của ảnh logo (Notifications/Assets/email-logo.png, 336x96, nền #1A4B3B).</summary>
    public const string LogoResourceName = "LegacyVault.Infrastructure.Notifications.email-logo.png";

    /// <param name="kicker">Nhãn nhỏ phía trên tiêu đề, ví dụ "Nhắc điểm danh". Null hoặc rỗng thì bỏ qua.</param>
    /// <param name="securityCallout">true thì ghi chú nằm trong khung vàng "Lưu ý bảo mật" (dùng cho đặt lại mật khẩu);
    /// false thì là dòng ghi chú thường dưới đường kẻ.</param>
    public static string BuildHtml(
        string title,
        string recipientName,
        string leadText,
        string actionUrl,
        string actionButtonText,
        string securityNotice,
        string? kicker = null,
        bool securityCallout = false)
    {
        string encodedTitle = WebUtility.HtmlEncode(title);
        string encodedName = WebUtility.HtmlEncode(recipientName);
        string encodedUrl = WebUtility.HtmlEncode(actionUrl);
        string encodedBtn = WebUtility.HtmlEncode(actionButtonText);
        string encodedNotice = WebUtility.HtmlEncode(securityNotice);

        StringBuilder lead = new StringBuilder();
        foreach (string line in leadText.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            lead.Append("<p style=\"margin:0 0 14px 0;font-size:16px;line-height:1.7;color:#2B332F;\">")
                .Append(WebUtility.HtmlEncode(line))
                .Append("</p>");
        }

        string kickerHtml = string.Empty;
        if (!string.IsNullOrWhiteSpace(kicker))
        {
            kickerHtml = "<p style=\"margin:0 0 6px 0;font-size:13px;line-height:1.5;color:#4D4D4B;\">"
                + WebUtility.HtmlEncode(kicker) + "</p>";
        }

        // Email thông báo thuần (không có hành động) truyền actionUrl rỗng: bỏ nút và khung liên kết.
        string actionHtml = string.Empty;
        if (!string.IsNullOrWhiteSpace(actionUrl))
        {
            actionHtml = $@"<table role=""presentation"" border=""0"" cellspacing=""0"" cellpadding=""0"" style=""margin:6px 0 22px 0;"">
<tr><td align=""center"" bgcolor=""#E0C486"" style=""background-color:#E0C486;border-radius:12px;"">
<a href=""{encodedUrl}"" target=""_blank"" style=""display:inline-block;padding:14px 26px;font-family:{FontStack};font-size:16px;font-weight:700;color:#0A281E;text-decoration:none;border-radius:12px;"">{encodedBtn}</a>
</td></tr>
</table>
<table role=""presentation"" width=""100%"" border=""0"" cellspacing=""0"" cellpadding=""0"" style=""margin:0 0 20px 0;"">
<tr><td style=""background-color:#FAFAF6;border:1px solid #E4E0D2;border-radius:12px;padding:14px 16px;font-family:{FontStack};"">
<p style=""margin:0 0 6px 0;font-size:13px;line-height:1.5;color:#4D4D4B;"">Nếu nút không bấm được, hãy sao chép liên kết này vào trình duyệt:</p>
<p style=""margin:0;font-size:13px;line-height:1.5;word-break:break-all;""><a href=""{encodedUrl}"" style=""color:#1A4B3B;text-decoration:underline;"">{encodedUrl}</a></p>
</td></tr>
</table>";
        }

        string noticeHtml;
        if (securityCallout)
        {
            noticeHtml = "<table role=\"presentation\" width=\"100%\" border=\"0\" cellspacing=\"0\" cellpadding=\"0\" style=\"margin:0;\">"
                + "<tr><td style=\"background-color:#F1E6CC;border-left:4px solid #E0C486;border-radius:8px;padding:14px 16px;"
                + "font-size:14px;line-height:1.6;color:#0A281E;\">"
                + "<strong>Lưu ý bảo mật:</strong> " + encodedNotice
                + "</td></tr></table>";
        }
        else
        {
            noticeHtml = "<p style=\"margin:0;border-top:1px solid #E4E0D2;padding-top:16px;font-size:13px;line-height:1.6;color:#4D4D4B;\">"
                + encodedNotice + "</p>";
        }

        return $@"<!DOCTYPE html>
<html lang=""vi"">
<head>
<meta charset=""UTF-8"">
<meta name=""viewport"" content=""width=device-width, initial-scale=1.0"">
<meta name=""color-scheme"" content=""light"">
<title>{encodedTitle}</title>
</head>
<body style=""margin:0;padding:0;background-color:#EFECE3;font-family:{FontStack};-webkit-font-smoothing:antialiased;"">
<table role=""presentation"" width=""100%"" border=""0"" cellspacing=""0"" cellpadding=""0"" style=""background-color:#EFECE3;"">
<tr><td align=""center"" style=""padding:32px 16px;"">
<table role=""presentation"" width=""100%"" border=""0"" cellspacing=""0"" cellpadding=""0"" style=""max-width:616px;background-color:#FFFFFF;border:1px solid #D9D4C4;border-radius:16px;overflow:hidden;"">
<tr><td bgcolor=""#1A4B3B"" style=""background-color:#1A4B3B;padding:18px 32px;font-family:{FontStack};"">
<img src=""cid:{LogoContentId}"" alt=""LegacyVault"" width=""168"" height=""48"" style=""display:block;border:0;outline:none;width:168px;height:48px;color:#EFECE3;font-size:20px;font-weight:600;"">
</td></tr>
<tr><td style=""padding:36px 32px 28px 32px;font-family:{FontStack};"">
{kickerHtml}
<h1 style=""margin:0 0 18px 0;font-size:28px;line-height:1.2;font-weight:600;color:#0A281E;"">{encodedTitle}</h1>
<p style=""margin:0 0 14px 0;font-size:16px;line-height:1.7;color:#2B332F;"">Chào {encodedName},</p>
{lead}
{actionHtml}
{noticeHtml}
</td></tr>
</table>
<p style=""margin:16px 0 0 0;font-family:{FontStack};font-size:12px;line-height:1.5;color:#4D4D4B;text-align:center;"">LegacyVault không thay thế di chúc.</p>
</td></tr>
</table>
</body>
</html>";
    }
}
