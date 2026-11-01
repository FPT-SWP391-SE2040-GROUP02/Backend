namespace LegacyVault.Application.Dtos.Responses;

/// <summary>Trạng thái điểm danh của kho (docs/api-contract.md mục 6.8).</summary>
public class CheckInStatusResponse
{
    /// <summary>NOT_STARTED | ACTIVE | CHECKIN_OVERDUE | AWAITING_CHECK | PAUSED, tính theo giờ server hiện tại.</summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>Trạng thái kế hoạch đang lưu trong DB (có thể chậm hơn Status vài giây khi job chưa chạy).</summary>
    public string PlanStatus { get; set; } = string.Empty;

    /// <summary>Giờ server (UTC) lúc trả kết quả; FE dùng để đếm ngược, không dùng giờ trình duyệt.</summary>
    public DateTimeOffset ServerTime { get; set; }

    public DateTimeOffset? LastCheckInAt { get; set; }

    /// <summary>Hạn điểm danh của kỳ đang mở; null khi chưa bắt đầu.</summary>
    public DateTimeOffset? NextCheckInDueAt { get; set; }

    /// <summary>Hết thời gian chờ của kỳ đang mở; null khi chưa bắt đầu.</summary>
    public DateTimeOffset? GraceEndsAt { get; set; }

    public CheckInSettingsResponse Settings { get; set; } = new CheckInSettingsResponse();

    public PaginatedList<CheckInPeriodResponse> History { get; set; } = new PaginatedList<CheckInPeriodResponse>();
}

public class CheckInSettingsResponse
{
    public int CycleDays { get; set; }

    public int GraceDays { get; set; }

    /// <summary>Chu kỳ sẽ áp dụng từ kỳ sau; null nếu không có thay đổi chờ.</summary>
    public int? PendingCycleDays { get; set; }

    public int? PendingGraceDays { get; set; }

    /// <summary>Gửi lại trong body (trường etag) khi PATCH cấu hình.</summary>
    public string Etag { get; set; } = string.Empty;
}

/// <summary>Một kỳ điểm danh trong lịch sử.</summary>
public class CheckInPeriodResponse
{
    public int Sequence { get; set; }

    public DateTimeOffset StartedAt { get; set; }

    public DateTimeOffset DueAt { get; set; }

    public DateTimeOffset GraceEndsAt { get; set; }

    /// <summary>Có giá trị = Owner đã điểm danh kết thúc kỳ này.</summary>
    public DateTimeOffset? CheckedInAt { get; set; }

    public DateTimeOffset? OverdueAt { get; set; }

    public DateTimeOffset? AwaitingCheckAt { get; set; }

    /// <summary>
    /// Lúc kỳ đóng; null khi kỳ còn mở. Có ClosedAt mà không có CheckedInAt nghĩa là kỳ bị dừng vì kế hoạch không còn
    /// chạy (ví dụ về Nháp theo SETUP-16), không phải Owner đã điểm danh.
    /// </summary>
    public DateTimeOffset? ClosedAt { get; set; }
}
