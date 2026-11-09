namespace LegacyVault.Domain.Enums;

/// <summary>
/// Các mốc nhắc điểm danh (DMS-06). Thứ tự khai báo = thứ tự thời gian; không đổi giá trị đã có.
/// </summary>
/// <remarks>
/// Hai mốc nhắc trước hạn, rồi tối đa năm thư nhắc trễ sau hạn. Số thư nhắc trễ và khoảng cách giữa các thư
/// do Admin đặt và được chốt vào từng kỳ (CheckInPeriod.MaxLateReminders, LateReminderGapDays).
/// GraceStarted và GraceTwoDaysLeft là mốc của cách làm cũ (thời gian chờ cố định), chỉ còn trong dữ liệu đã lưu.
/// </remarks>
public enum CheckInReminderKind
{
    /// <summary>Trước hạn điểm danh 7 ngày.</summary>
    DueInSevenDays = 0,
    /// <summary>Trước hạn điểm danh 1 ngày.</summary>
    DueInOneDay = 1,
    /// <summary>Cũ: đã tới hạn, bắt đầu thời gian chờ. Không còn gửi mới.</summary>
    GraceStarted = 2,
    /// <summary>Cũ: thời gian chờ còn 2 ngày. Không còn gửi mới.</summary>
    GraceTwoDaysLeft = 3,
    /// <summary>Thư nhắc trễ lần 1, gửi đúng lúc tới hạn.</summary>
    LateReminder1 = 4,
    /// <summary>Thư nhắc trễ lần 2.</summary>
    LateReminder2 = 5,
    /// <summary>Thư nhắc trễ lần 3.</summary>
    LateReminder3 = 6,
    /// <summary>Thư nhắc trễ lần 4.</summary>
    LateReminder4 = 7,
    /// <summary>Thư nhắc trễ lần 5.</summary>
    LateReminder5 = 8,
}
