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

    /// <summary>Mật khẩu ứng dụng Gmail (không phải mật khẩu tài khoản).</summary>
    public string Password { get; set; } = string.Empty;

    /// <summary>Địa chỉ người gửi; để trống thì dùng UserName (Gmail chỉ cho gửi từ tài khoản đã đăng nhập hoặc bí danh đã xác minh).</summary>
    public string FromAddress { get; set; } = string.Empty;

    public string FromName { get; set; } = "LegacyVault";

    public int TimeoutSeconds { get; set; } = 20;

    /// <summary>Đủ Host, UserName và Password thì mới gửi email thật.</summary>
    public bool IsConfigured
    {
        get
        {
            return !string.IsNullOrWhiteSpace(Host)
                && !string.IsNullOrWhiteSpace(UserName)
                && !string.IsNullOrWhiteSpace(Password);
        }
    }

    public string ResolveFromAddress()
    {
        if (!string.IsNullOrWhiteSpace(FromAddress))
        {
            return FromAddress;
        }
        return UserName;
    }

    /// <summary>Đổi SecureSocket (chữ) sang giá trị của MailKit; sai tên → null.</summary>
    public SecureSocketOptions? ParseSecureSocket()
    {
        SecureSocketOptions value;
        if (Enum.TryParse(SecureSocket, ignoreCase: true, out value))
        {
            return value;
        }
        return null;
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
