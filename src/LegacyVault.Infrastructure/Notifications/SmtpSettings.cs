using MailKit.Security;
using MimeKit;

namespace LegacyVault.Infrastructure.Notifications;

/// <summary>
/// Cấu hình mục "Email:Smtp" trong appsettings: dịch vụ gửi email qua SMTP (mặc định Gmail, dùng MailKit).
/// </summary>
/// <remarks>
/// - UserName và Password là BÍ MẬT: không viết vào appsettings.json hay commit. Khi dev đặt bằng
///   <c>dotnet user-secrets set "Email:Smtp:UserName" "..." --project src/LegacyVault.Api</c>; server thật đặt bằng biến môi trường
///   <c>Email__Smtp__UserName</c> và <c>Email__Smtp__Password</c>.
/// - Gmail không cho dùng mật khẩu đăng nhập thường: cần bật xác minh 2 bước rồi tạo "Mật khẩu ứng dụng"
///   (App password, 16 ký tự) và đặt vào Password.
/// - Thiếu UserName hoặc Password thì coi như chưa cấu hình: hệ thống dùng bản chỉ ghi log (không gửi email thật).
/// </remarks>
public sealed class SmtpSettings
{
    public const string SectionName = "Email:Smtp";

    public string Host { get; set; } = "smtp.gmail.com";

    /// <summary>587 + StartTls (mặc định của Gmail), hoặc 465 + SslOnConnect.</summary>
    public int Port { get; set; } = 587;

    /// <summary>Tên giá trị của MailKit SecureSocketOptions: None, Auto, SslOnConnect, StartTls, StartTlsWhenAvailable.</summary>
    public string SecureSocket { get; set; } = "StartTls";

    /// <summary>Địa chỉ Gmail dùng để đăng nhập SMTP.</summary>
    public string UserName { get; set; } = string.Empty;

    public string ResolveUserName()
    {
        return (UserName ?? string.Empty).Trim().Trim('"').Trim('\'');
    }

    public string ResolveHost()
    {
        string host = (Host ?? string.Empty).Trim().Trim('"').Trim('\'');
        return string.IsNullOrWhiteSpace(host) ? "smtp.gmail.com" : host;
    }

    /// <summary>Mật khẩu ứng dụng Gmail (không phải mật khẩu tài khoản). Tự động loại bỏ khoảng trắng nếu copy từ Google.</summary>
    public string Password { get; set; } = string.Empty;

    public string ResolvePassword()
    {
        return (Password ?? string.Empty).Trim().Trim('"').Trim('\'').Replace(" ", "").Trim();
    }

    /// <summary>Địa chỉ người gửi; để trống thì dùng UserName (Gmail chỉ cho gửi từ tài khoản đã đăng nhập hoặc bí danh đã xác minh).</summary>
    public string FromAddress { get; set; } = string.Empty;

    public string FromName { get; set; } = "LegacyVault";

    public int TimeoutSeconds { get; set; } = 20;

    /// <summary>
    /// URL Webhook chuyển tiếp email qua HTTPS (ví dụ Google Apps Script Web App).
    /// Giúp vượt qua việc Render chặn cổng SMTP 25, 465, 587 trên gói Free.
    /// </summary>
    public string WebhookUrl { get; set; } = string.Empty;

    public string ResolveWebhookUrl()
    {
        return (WebhookUrl ?? string.Empty).Trim().Trim('"').Trim('\'');
    }

    /// <summary>
    /// API Key của Brevo (Sendinblue) để gửi email qua HTTPS port 443 không bị firewall chặn.
    /// </summary>
    public string BrevoApiKey { get; set; } = string.Empty;

    public string ResolveBrevoApiKey()
    {
        return (BrevoApiKey ?? string.Empty).Trim().Trim('"').Trim('\'');
    }

    /// <summary>Đủ WebhookUrl, BrevoApiKey, hoặc Host/UserName/Password thì mới gửi email thật.</summary>
    public bool IsConfigured
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(ResolveWebhookUrl()) || !string.IsNullOrWhiteSpace(ResolveBrevoApiKey()))
            {
                return true;
            }
            return !string.IsNullOrWhiteSpace(ResolveHost())
                && !string.IsNullOrWhiteSpace(ResolveUserName())
                && !string.IsNullOrWhiteSpace(ResolvePassword());
        }
    }

    public string ResolveFromAddress()
    {
        string from = (FromAddress ?? string.Empty).Trim().Trim('"').Trim('\'');
        if (!string.IsNullOrWhiteSpace(from))
        {
            return from;
        }
        return ResolveUserName();
    }

    /// <summary>Đổi SecureSocket (chữ) sang giá trị của MailKit; sai tên → null.</summary>
    public SecureSocketOptions? ParseSecureSocket()
    {
        string cleaned = (SecureSocket ?? string.Empty).Trim().Trim('"').Trim('\'');
        if (Port == 465 && (string.IsNullOrWhiteSpace(cleaned) || cleaned.Equals("StartTls", StringComparison.OrdinalIgnoreCase)))
        {
            return SecureSocketOptions.SslOnConnect;
        }
        if (Port == 587 && (string.IsNullOrWhiteSpace(cleaned) || cleaned.Equals("SslOnConnect", StringComparison.OrdinalIgnoreCase)))
        {
            return SecureSocketOptions.StartTls;
        }
        if (Enum.TryParse(cleaned, ignoreCase: true, out SecureSocketOptions value))
        {
            return value;
        }
        return Port == 465 ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTls;
    }

    /// <summary>
    /// Kiểm tra khi khởi động. Trả câu báo lỗi (không chứa mật khẩu); hợp lệ hoặc chưa cấu hình thì trả null.
    /// </summary>
    public string? FindError()
    {
        if (!IsConfigured)
        {
            return null;
        }
        if (Port < 1 || Port > 65535)
        {
            return "Email:Smtp:Port phải từ 1 đến 65535.";
        }
        if (ParseSecureSocket() == null)
        {
            return "Email:Smtp:SecureSocket phải là None, Auto, SslOnConnect, StartTls hoặc StartTlsWhenAvailable.";
        }
        MailboxAddress? mailbox;
        // MimeKit chấp nhận cả chuỗi không có "@", nên phải kiểm tra thêm có phần tên và phần miền.
        if (!MailboxAddress.TryParse(ResolveFromAddress(), out mailbox) || !HasLocalAndDomainParts(mailbox.Address))
        {
            return "Email:Smtp:FromAddress (hoặc UserName) không phải địa chỉ email hợp lệ.";
        }
        return null;
    }

    private static bool HasLocalAndDomainParts(string address)
    {
        int at = address.IndexOf('@');
        return at > 0 && at < address.Length - 1;
    }
}
