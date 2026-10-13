namespace LegacyVault.Application.Notifications;

/// <summary>
/// Gửi email hệ thống. Cài đặt ở Infrastructure (SMTP/nhà cung cấp email).
/// </summary>
/// <remarks>
/// OPS-04: email chỉ chứa metadata cần thiết; không chứa mật khẩu, nội dung tài sản.
/// Không ghi link/token vào log (SEC-06).
/// </remarks>
public interface IEmailSender
{
    /// <summary>Gửi link xác minh email (AUTH-04). verificationUrl đã chứa token gốc.</summary>
    Task SendEmailVerificationAsync(string toEmail, string fullName, string verificationUrl, CancellationToken ct);
}
