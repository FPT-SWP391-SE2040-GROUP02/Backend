namespace LegacyVault.Application.Dtos.Responses;

/// <summary>Một mốc có thể tua trong công cụ thử nghiệm.</summary>
public class DevMilestoneResponse
{
    /// <summary>
    /// DUE_REMINDER_n (thư nhắc trước hạn thứ n) | CHECKIN_OVERDUE (quá hạn, thư nhắc trễ lần 1)
    /// | LATE_REMINDER_n (thư nhắc trễ lần n, từ 2) | GRACE_ENDED | SERVICE_EXPIRED | RECIPIENT_DECISION_ENDED.
    /// </summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>AVAILABLE (bấm tua được) | PASSED (đã qua mốc) | NOT_APPLICABLE (trạng thái hiện tại không có mốc này).</summary>
    public string State { get; set; } = string.Empty;

    /// <summary>Lý do không tua được, để hiện dưới mốc; rỗng khi tua được hoặc đã qua.</summary>
    public string Note { get; set; } = string.Empty;

    /// <summary>Thư nhắc trước hạn gửi trước hạn điểm danh bao nhiêu ngày; mốc khác là null.</summary>
    public int? DaysBeforeDue { get; set; }
}
