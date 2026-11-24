namespace LegacyVault.Application.Notifications;

/// <summary>Thư gửi mã nhận cho người nhận. Khóa gốc chỉ đi qua đây, không lưu DB và không ghi log.</summary>
public sealed class HandoverKeyMail
{
    public Guid TurnId { get; set; }

    public string ToEmail { get; set; } = string.Empty;

    public string FullName { get; set; } = string.Empty;

    /// <summary>Mã nhận dạng người dùng gõ vào, ví dụ "XXXX-XXXX-XXXX-XXXX".</summary>
    public string Key { get; set; } = string.Empty;
}
