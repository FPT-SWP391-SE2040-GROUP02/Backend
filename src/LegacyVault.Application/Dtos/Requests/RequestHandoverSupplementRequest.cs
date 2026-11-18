using System.ComponentModel.DataAnnotations;

namespace LegacyVault.Application.Dtos.Requests;

/// <summary>Body của POST /api/v1/verifier/handover-submissions/{id}/request-supplement.</summary>
public class RequestHandoverSupplementRequest
{
    [Required(ErrorMessage = "Vui lòng nhập lý do.")]
    [MaxLength(1000)]
    public string Reason { get; set; } = string.Empty;
}
