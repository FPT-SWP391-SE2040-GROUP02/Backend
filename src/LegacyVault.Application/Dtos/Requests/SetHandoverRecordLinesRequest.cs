using System.ComponentModel.DataAnnotations;

namespace LegacyVault.Application.Dtos.Requests;

/// <summary>Body của PUT /api/v1/verifier/handover-records/{id}/lines: thay toàn bộ danh sách "ai nhận tài sản nào".</summary>
public class SetHandoverRecordLinesRequest
{
    [Required]
    public List<HandoverRecordLineRequest> Lines { get; set; } = new List<HandoverRecordLineRequest>();
}

/// <summary>Body của POST /api/v1/verifier/handover-records/{id}/finalize: luật sư chốt biên bản.</summary>
public class FinalizeHandoverRecordRequest
{
    /// <summary>
    /// Luật sư xác nhận đã kiểm tra di chúc (bản sau cùng, hợp pháp) hoặc xác định không có di chúc, lập biên bản đúng quy định
    /// và chịu trách nhiệm về nội dung. Phải là true; false hoặc thiếu thì 400 VALIDATION_ERROR.
    /// </summary>
    public bool? ConfirmLegalBasis { get; set; }
}

/// <summary>Một dòng biên bản: người nhận nhận một tài sản.</summary>
public class HandoverRecordLineRequest
{
    public Guid BeneficiaryId { get; set; }
    public Guid AssetId { get; set; }
}
