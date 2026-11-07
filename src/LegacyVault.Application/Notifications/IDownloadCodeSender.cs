namespace LegacyVault.Application.Notifications;

/// <summary>Gửi mã tải dùng một lần cho người nhận qua email. Cài đặt ở Infrastructure; gửi lỗi thì ném exception.</summary>
/// <remarks>Thư chỉ có mã và hạn dùng, không có tên gói hay tên tài sản (OPS-04).</remarks>
public interface IDownloadCodeSender
{
    Task SendAsync(string toEmail, string fullName, string code, DateTimeOffset expiresAt, CancellationToken ct);
}
