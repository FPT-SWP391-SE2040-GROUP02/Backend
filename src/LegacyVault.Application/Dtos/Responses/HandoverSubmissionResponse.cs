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
