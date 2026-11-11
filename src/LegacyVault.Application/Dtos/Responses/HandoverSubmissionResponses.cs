namespace LegacyVault.Application.Dtos.Responses;

/// <summary>Hồ sơ người nhận nhìn từ phía người nhận: trạng thái và lý do cần bổ sung (không có kết quả đối chiếu).</summary>
public class HandoverSubmissionResponse
{
    public Guid Id { get; set; }

    public Guid TurnId { get; set; }

    /// <summary>ACCEPT | DECLINE.</summary>
    public string Kind { get; set; } = string.Empty;

    /// <summary>SUBMITTED | SUPPLEMENT_REQUESTED | APPROVED | SUPERSEDED.</summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>Có khi Người xét duyệt yêu cầu bổ sung.</summary>
    public string? SupplementReason { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>Một dòng trong hàng chờ duyệt hồ sơ người nhận của Người xét duyệt.</summary>
public class VerifierHandoverSubmissionSummaryResponse
{
    public Guid Id { get; set; }

    public Guid TurnId { get; set; }

    public string ContainerName { get; set; } = string.Empty;

    /// <summary>ACCEPT | DECLINE.</summary>
    public string Kind { get; set; } = string.Empty;

    /// <summary>true khi cả ba trường (họ tên, số căn cước, ngày sinh) khớp thông tin Chủ kho đã khai.</summary>
    public bool AllMatch { get; set; }

    public DateTimeOffset SubmittedAt { get; set; }
}

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

    /// <summary>Id biên bản mà hồ sơ này thuộc về; null với hồ sơ của luồng cũ chưa có biên bản.</summary>
    public Guid? DocumentId { get; set; }

    /// <summary>true khi duyệt được: hồ sơ đang chờ, ba trường khớp và lượt còn ở bước danh tính.</summary>
    public bool CanApprove { get; set; }

    /// <summary>true khi yêu cầu bổ sung được: hồ sơ đang chờ và lượt còn ở bước danh tính.</summary>
    public bool CanRequestSupplement { get; set; }
}
