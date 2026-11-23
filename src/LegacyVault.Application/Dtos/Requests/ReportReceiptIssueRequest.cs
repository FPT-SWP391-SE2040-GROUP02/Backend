using System.ComponentModel.DataAnnotations;

namespace LegacyVault.Application.Dtos.Requests;

/// <summary>Người nhận báo tài sản thiếu hoặc sai (POST /handover/turns/{id}/receipt-issue).</summary>
public class ReportReceiptIssueRequest
{
    [Required(ErrorMessage = "Vui lòng ghi rõ tài sản nào thiếu hoặc sai.")]
    [MaxLength(1000)]
    public string Note { get; set; } = string.Empty;
}
