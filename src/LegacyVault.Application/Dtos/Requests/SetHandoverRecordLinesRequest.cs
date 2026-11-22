using System.ComponentModel.DataAnnotations;

namespace LegacyVault.Application.Dtos.Requests;

/// <summary>Body của PUT /api/v1/verifier/handover-records/{id}/lines: thay toàn bộ danh sách "ai nhận tài sản nào".</summary>
public class SetHandoverRecordLinesRequest
{
    [Required]
    public List<HandoverRecordLineRequest> Lines { get; set; } = new List<HandoverRecordLineRequest>();
}

/// <summary>Một dòng biên bản: người nhận nhận một tài sản.</summary>
public class HandoverRecordLineRequest
{
    public Guid BeneficiaryId { get; set; }
    public Guid AssetId { get; set; }
}
