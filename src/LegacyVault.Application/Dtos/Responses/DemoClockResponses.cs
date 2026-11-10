namespace LegacyVault.Application.Dtos.Responses;

/// <summary>Đồng hồ của hệ thống trong chế độ DEMO: giờ hệ thống đang dùng và giờ thật.</summary>
public class DemoClockStatusResponse
{
    /// <summary>Giờ hệ thống đang dùng (UTC) = giờ thật + độ lệch.</summary>
    public DateTimeOffset NowUtc { get; set; }

    /// <summary>Giờ thật của máy chủ (UTC).</summary>
    public DateTimeOffset RealNowUtc { get; set; }

    /// <summary>Độ lệch giữa giờ hệ thống và giờ thật, tính bằng giây.</summary>
    public long OffsetSeconds { get; set; }

    /// <summary>true khi độ lệch khác 0, tức hệ thống đang giả lập một ngày khác ngày thật.</summary>
    public bool IsSimulated { get; set; }
}

/// <summary>Một bước đã chạy trong lần tua: hệ thống dừng ở một mốc rồi chạy các việc nền đúng một lần.</summary>
public class DemoClockStepResponse
{
    /// <summary>Giờ hệ thống (UTC) lúc chạy bước này.</summary>
    public DateTimeOffset At { get; set; }

    /// <summary>
    /// Loại mốc: CHECK_IN_REMINDER, CHECK_IN_DUE, CHECK_IN_GRACE_END, EXECUTOR_INVITATION, SERVICE_EXPIRY,
    /// HANDOVER_DEADLINE, INCIDENT hoặc TARGET_DAY (ngày đích).
    /// </summary>
    public string Kind { get; set; } = string.Empty;

    /// <summary>Số mục việc nền đã xử lý ở bước này.</summary>
    public int ItemsProcessed { get; set; }

    /// <summary>Số email nhắc đã gửi ở bước này (đếm được với thư nhắc điểm danh và thư nhắc lời mời).</summary>
    public int EmailsSent { get; set; }

    /// <summary>Số mục xử lý lỗi (không dừng lần tua).</summary>
    public int Failures { get; set; }
}

/// <summary>Kết quả một lần tua: trạng thái đồng hồ sau khi tua và danh sách bước đã chạy theo thứ tự thời gian.</summary>
public class DemoClockTravelResponse
{
    public DemoClockStatusResponse Clock { get; set; } = new DemoClockStatusResponse();

    public List<DemoClockStepResponse> Steps { get; set; } = new List<DemoClockStepResponse>();
}
