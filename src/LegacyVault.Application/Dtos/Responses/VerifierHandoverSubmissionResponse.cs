namespace LegacyVault.Application.Dtos.Responses;

/// <summary>Chi tiết một hồ sơ người nhận cho Người xét duyệt. Không có họ tên, ngày sinh hay số căn cước đầy đủ.</summary>
public class VerifierHandoverSubmissionResponse : VerifierHandoverSubmissionSummaryResponse
{
    /// <summary>SUBMITTED | SUPPLEMENT_REQUESTED | APPROVED | SUPERSEDED.</summary>
    public string Status { get; set; } = string.Empty;

    public bool NameMatches { get; set; }

    public bool CitizenIdMatches { get; set; }

    public bool DateOfBirthMatches { get; set; }

    public string CitizenId { get; set; } = string.Empty;

    /// <summary>Họ tên người nhận đã gõ; rỗng với hồ sơ cũ.</summary>
    public string ClaimedFullName { get; set; } = string.Empty;

    /// <summary>Ngày sinh người nhận đã gõ; null với hồ sơ cũ.</summary>
    public DateOnly? ClaimedDateOfBirth { get; set; }

    /// <summary>Họ tên Chủ kho đã khai cho người nhận này; rỗng khi không đọc được.</summary>
    public string DeclaredFullName { get; set; } = string.Empty;

    /// <summary>Số căn cước Chủ kho đã khai cho người nhận này; rỗng khi không đọc được.</summary>
    public string DeclaredCitizenId { get; set; } = string.Empty;

    /// <summary>Ngày sinh Chủ kho đã khai cho người nhận này; null khi không đọc được.</summary>
    public DateOnly? DeclaredDateOfBirth { get; set; }

    public bool Acknowledged { get; set; }

    public bool VoluntaryDecline { get; set; }

    public string? SupplementReason { get; set; }

    /// <summary>Số tài sản của lượt (người nhận chỉ nhận khi chấp nhận).</summary>
    public int ItemCount { get; set; }


    /// <summary>true khi duyệt được: hồ sơ đang chờ, ba trường khớp và lượt còn ở bước danh tính.</summary>
    public bool CanApprove { get; set; }

    /// <summary>true khi yêu cầu bổ sung được: hồ sơ đang chờ và lượt còn ở bước danh tính.</summary>
    public bool CanRequestSupplement { get; set; }
}
