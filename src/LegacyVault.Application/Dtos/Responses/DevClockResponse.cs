namespace LegacyVault.Application.Dtos.Responses;

/// <summary>Trạng thái thời gian của một kho thử trong công cụ thử nghiệm.</summary>
public class DevClockResponse
{
    public Guid VaultId { get; set; }

    /// <summary>Trạng thái kế hoạch bàn giao, cùng bộ giá trị với DevVaultOptionResponse.PlanStatus.</summary>
    public string PlanStatus { get; set; } = string.Empty;

    /// <summary>ON_TIME (đúng hạn) | OVERDUE (đã qua hạn điểm danh) | NOT_RUNNING (lịch điểm danh không chạy).</summary>
    public string CheckInState { get; set; } = string.Empty;

    /// <summary>REAL_TIME (thời gian thực) | SHIFTED (đã tua, chưa về thời gian hiện tại).</summary>
    public string ClockState { get; set; } = string.Empty;

    /// <summary>Số thư nhắc trễ tối đa của kỳ đang mở (chốt lúc mở kỳ); không có kỳ đang mở thì null.</summary>
    public int? MaxLateReminders { get; set; }

    /// <summary>Số ngày giữa hai thư nhắc trễ của kỳ đang mở; không có kỳ đang mở thì null.</summary>
    public int? LateReminderGapDays { get; set; }

    public List<DevMilestoneResponse> Milestones { get; set; } = new List<DevMilestoneResponse>();
}
