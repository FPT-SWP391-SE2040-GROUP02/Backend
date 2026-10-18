namespace LegacyVault.Domain.Enums;

/// <summary>
/// Bốn mốc nhắc điểm danh (DMS-06). Thứ tự khai báo = thứ tự thời gian; không đổi giá trị đã có.
/// </summary>
public enum CheckInReminderKind
{
    /// <summary>Trước hạn điểm danh 7 ngày.</summary>
    DueInSevenDays = 0,
    /// <summary>Trước hạn điểm danh 1 ngày.</summary>
    DueInOneDay = 1,
    /// <summary>Đã tới hạn, bắt đầu thời gian chờ.</summary>
    GraceStarted = 2,
    /// <summary>Thời gian chờ còn 2 ngày.</summary>
    GraceTwoDaysLeft = 3,
}
