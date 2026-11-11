namespace LegacyVault.Domain.Enums;

/// <summary>
/// Các mốc nhắc điểm danh (DMS-06). Thứ tự khai báo = thứ tự thời gian; không đổi giá trị đã có.
/// </summary>
/// <remarks>
/// Tối đa năm mốc nhắc trước hạn (hai mốc đầu mang tên cũ, ba mốc sau là DueReminder3 đến DueReminder5), rồi tối đa năm thư nhắc
/// trễ sau hạn. Số thư nhắc trước hạn và số ngày của từng thư do Admin đặt, áp dụng ngay cho cả kỳ đang chạy. Số thư nhắc trễ và
/// khoảng cách giữa các thư do Admin đặt và được chốt vào từng kỳ (CheckInPeriod.MaxLateReminders, LateReminderGapDays).
/// Giá trị số của enum KHÔNG phản ánh thứ tự thời gian: dùng CheckInTimeline.ActiveKinds để có thứ tự đúng.
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
    /// <summary>Thư nhắc trước hạn thứ ba (gần hạn hơn thư thứ hai). Chỉ gửi khi Admin đặt từ 3 thư nhắc trước hạn trở lên.</summary>
    DueReminder3 = 9,
    /// <summary>Thư nhắc trước hạn thứ tư.</summary>
    DueReminder4 = 10,
    /// <summary>Thư nhắc trước hạn thứ năm.</summary>
    DueReminder5 = 11,
}
