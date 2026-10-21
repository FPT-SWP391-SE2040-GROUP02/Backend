using System.ComponentModel.DataAnnotations;

namespace LegacyVault.Application.Dtos.Requests;

/// <summary>Admin chọn mốc thời gian cần tua cho một kho thử (công cụ thử nghiệm, chỉ có ở Development).</summary>
public class DevTimeTravelRequest
{
    /// <summary>CHECKIN_OVERDUE | GRACE_ENDED | OBJECTION_WINDOW_ENDED | SELFIE_DEADLINE_PASSED.</summary>
    [Required(ErrorMessage = "Vui lòng chọn mốc cần tua.")]
    [MaxLength(32)]
    public string Milestone { get; set; } = string.Empty;
}
