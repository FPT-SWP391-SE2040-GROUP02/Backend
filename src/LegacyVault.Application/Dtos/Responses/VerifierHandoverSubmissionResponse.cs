namespace LegacyVault.Application.Dtos.Responses;

/// <summary>Chi tiết một hồ sơ người nhận cho Người xét duyệt. Không có họ tên, ngày sinh hay số căn cước đầy đủ.</summary>
public class VerifierHandoverSubmissionResponse : VerifierHandoverSubmissionSummaryResponse
{
    /// <summary>SUBMITTED | SUPPLEMENT_REQUESTED | APPROVED | SUPERSEDED.</summary>
    public string Status { get; set; } = string.Empty;

    public bool NameMatches { get; set; }

    public bool CitizenIdMatches { get; set; }

    public bool DateOfBirthMatches { get; set; }

    public string CitizenIdLast4 { get; set; } = string.Empty;

    public bool Acknowledged { get; set; }

    public bool VoluntaryDecline { get; set; }

    public string? SupplementReason { get; set; }

    /// <summary>Số tài sản trong gói (người nhận chỉ nhận khi chấp nhận).</summary>
    public int ItemCount { get; set; }


    /// <summary>true khi duyệt được: hồ sơ đang chờ, ba trường khớp và lượt còn ở bước danh tính.</summary>
    public bool CanApprove { get; set; }

    /// <summary>true khi yêu cầu bổ sung được: hồ sơ đang chờ và lượt còn ở bước danh tính.</summary>
    public bool CanRequestSupplement { get; set; }
}
