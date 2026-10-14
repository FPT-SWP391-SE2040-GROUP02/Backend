namespace LegacyVault.Domain.Enums;

/// <summary>Trạng thái gửi một lời nhắc điểm danh.</summary>
public enum CheckInReminderStatus
{
    /// <summary>Đã ghi nhận cần gửi nhưng chưa gửi được; vòng quét sau sẽ thử lại.</summary>
    Pending = 0,
    /// <summary>Đã gửi thành công (mỗi mốc của mỗi kỳ chỉ gửi thành công một lần, TIME-02).</summary>
    Sent = 1,
    /// <summary>Bỏ qua vì đã có mốc mới hơn (ví dụ job tắt lâu), tránh gửi dồn nhiều email.</summary>
    Skipped = 2,
}
