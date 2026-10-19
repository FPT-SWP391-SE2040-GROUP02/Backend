using System.Net;

namespace LegacyVault.Infrastructure.Notifications;

/// <summary>
/// Mẫu HTML Email chuẩn thương hiệu Heritage của LegacyVault:
/// Màu chủ đạo: Forest Green (#0B291E), Champagne Gold (#B88E4C), Nền sáng (#FAF9F5).
/// Tương thích tốt với mọi ứng dụng email (Gmail, Outlook, Apple Mail).
/// </summary>
public static class LegacyVaultEmailTheme
{
    public static string BuildHtml(
        string title,
        string recipientName,
        string leadText,
        string actionUrl,
        string actionButtonText,
        string securityNotice)
    {
        string encodedTitle = WebUtility.HtmlEncode(title);
        string encodedName = WebUtility.HtmlEncode(recipientName);
        string encodedLead = WebUtility.HtmlEncode(leadText);
        string encodedUrl = WebUtility.HtmlEncode(actionUrl);
        string encodedBtn = WebUtility.HtmlEncode(actionButtonText);
        string encodedNotice = WebUtility.HtmlEncode(securityNotice);

        return $@"<!DOCTYPE html>
<html lang=""vi"">
<head>
  <meta charset=""UTF-8"">
  <meta name=""viewport"" content=""width=device-width, initial-scale=1.0"">
  <title>{encodedTitle}</title>
</head>
<body style=""margin:0; padding:0; background-color:#FAF9F5; font-family:-apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, Helvetica, Arial, sans-serif; color:#1A202C; -webkit-font-smoothing:antialiased;"">
  <table role=""presentation"" width=""100%"" border=""0"" cellspacing=""0"" cellpadding=""0"" style=""background-color:#FAF9F5; padding:40px 16px;"">
    <tr>
      <td align=""center"">
        <!-- Container 600px -->
        <table role=""presentation"" width=""100%"" border=""0"" cellspacing=""0"" cellpadding=""0"" style=""max-width:600px; background-color:#FFFFFF; border-radius:12px; overflow:hidden; border:1px solid #E8E6DF; box-shadow:0 4px 16px rgba(11,41,30,0.06);"">
          <!-- Header Brand -->
          <tr>
            <td style=""background-color:#0B291E; padding:32px 40px; text-align:center; border-bottom:3px solid #B88E4C;"">
              <div style=""color:#FFFFFF; font-size:22px; font-weight:700; letter-spacing:3px; text-transform:uppercase; margin:0 0 6px 0;"">
                LEGACYVAULT
              </div>
              <div style=""color:#B88E4C; font-size:12px; font-weight:500; letter-spacing:1px; text-transform:uppercase;"">
                Hệ Thống Quản Lý &amp; Bàn Giao Di Sản Số
              </div>
            </td>
          </tr>

          <!-- Main Content -->
          <tr>
            <td style=""padding:40px;"">
              <h1 style=""font-size:20px; font-weight:700; color:#0B291E; margin:0 0 20px 0; line-height:1.4;"">
                {encodedTitle}
              </h1>

              <p style=""font-size:15px; line-height:1.6; color:#2D3748; margin:0 0 16px 0;"">
                Kính chào <strong>{encodedName}</strong>,
              </p>

              <p style=""font-size:15px; line-height:1.6; color:#2D3748; margin:0 0 28px 0;"">
                {encodedLead}
              </p>

              <!-- CTA Button -->
              <table role=""presentation"" border=""0"" cellspacing=""0"" cellpadding=""0"" style=""margin:0 auto 32px auto;"">
                <tr>
                  <td align=""center"" style=""border-radius:6px; background-color:#0B291E;"">
                    <a href=""{encodedUrl}"" target=""_blank"" style=""display:inline-block; padding:14px 36px; font-size:15px; font-weight:600; color:#FFFFFF; text-decoration:none; border-radius:6px; border:1px solid #B88E4C; letter-spacing:0.5px;"">
                      {encodedBtn}
                    </a>
                  </td>
                </tr>
              </table>

              <!-- Fallback Link -->
              <p style=""font-size:12px; line-height:1.5; color:#718096; margin:0 0 24px 0; word-break:break-all;"">
                Nếu nút bấm trên không hoạt động, bạn có thể sao chép và dán liên kết sau vào trình duyệt:<br>
                <a href=""{encodedUrl}"" style=""color:#0B291E; text-decoration:underline;"">{encodedUrl}</a>
              </p>

              <!-- Security Callout Box -->
              <div style=""background-color:#FAF9F5; border-left:4px solid #B88E4C; padding:16px; border-radius:0 8px 8px 0; margin:0;"">
                <p style=""font-size:13px; line-height:1.5; color:#4A5568; margin:0;"">
                  <strong style=""color:#0B291E;"">&#128274; Lưu ý bảo mật:</strong> {encodedNotice}
                </p>
              </div>
            </td>
          </tr>

          <!-- Footer -->
          <tr>
            <td style=""background-color:#F5F4EE; padding:24px 40px; text-align:center; border-top:1px solid #E8E6DF;"">
              <p style=""font-size:12px; line-height:1.6; color:#718096; margin:0 0 8px 0;"">
                Email này được gửi tự động từ hệ thống <strong>LegacyVault</strong>.<br>
                Vui lòng không trả lời trực tiếp email này.
              </p>
              <p style=""font-size:11px; color:#A0AEC0; margin:0;"">
                &copy; 2026 LegacyVault Inc. Bảo mật chuẩn ISO/IEC 27001. Bảo lưu mọi quyền.
              </p>
            </td>
          </tr>
        </table>
      </td>
    </tr>
  </table>
</body>
</html>";
    }
}
