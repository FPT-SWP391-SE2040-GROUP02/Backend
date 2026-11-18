namespace LegacyVault.Application.Dtos.Responses;

/// <summary>Người xét duyệt đã duyệt hồ sơ chứng tử của kho: họ tên và số thẻ luật sư để người nhận tự kiểm chứng.</summary>
public class HandoverReviewerResponse
{
    public string FullName { get; set; } = string.Empty;

    public string? LawyerCardNumber { get; set; }
}
