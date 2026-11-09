namespace LegacyVault.Application.Dtos.Responses;

/// <summary>Một mốc có thể tua trong công cụ thử nghiệm.</summary>
public class DevMilestoneResponse
{
    /// <summary>CHECKIN_OVERDUE | GRACE_ENDED | SERVICE_EXPIRED | RECIPIENT_DECISION_ENDED.</summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>AVAILABLE (bấm tua được) | PASSED (đã qua mốc) | NOT_APPLICABLE (trạng thái hiện tại không có mốc này).</summary>
    public string State { get; set; } = string.Empty;

    /// <summary>Lý do không tua được, để hiện dưới mốc; rỗng khi tua được hoặc đã qua.</summary>
    public string Note { get; set; } = string.Empty;
}
