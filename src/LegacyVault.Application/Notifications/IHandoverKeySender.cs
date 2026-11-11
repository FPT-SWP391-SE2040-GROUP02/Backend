namespace LegacyVault.Application.Notifications;

/// <summary>Thư gửi khóa mở gói cho người nhận. Khóa gốc chỉ đi qua đây, không lưu DB và không ghi log (SEC-07).</summary>
public sealed class HandoverKeyMail
{
    public Guid TurnId { get; set; }

    public string ToEmail { get; set; } = string.Empty;

    public string FullName { get; set; } = string.Empty;

    /// <summary>Khóa mở gói dạng người dùng gõ vào, ví dụ "XXXX-XXXX-XXXX-XXXX".</summary>
    public string Key { get; set; } = string.Empty;
}

/// <summary>Gửi khóa mở gói cho người nhận. Cài đặt ở Infrastructure (email); gửi lỗi thì ném exception.</summary>
public interface IHandoverKeySender
{
    Task SendAsync(HandoverKeyMail mail, CancellationToken ct);
}
