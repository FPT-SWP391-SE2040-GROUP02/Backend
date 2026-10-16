using System.ComponentModel.DataAnnotations;

namespace LegacyVault.Application.Dtos.Requests;

/// <summary>Verifier kết luận báo còn sống (ALIVE-05).</summary>
public class ConcludeObjectionRequest
{
    /// <summary>ALIVE_CONFIRMED | OBJECTION_REJECTED | INSUFFICIENT_EVIDENCE.</summary>
    [Required(ErrorMessage = "Vui lòng chọn kết luận.")]
    [MaxLength(32)]
    public string Conclusion { get; set; } = string.Empty;

    [Required(ErrorMessage = "Vui lòng ghi lý do.")]
    [MaxLength(1000)]
    public string Reason { get; set; } = string.Empty;
}
