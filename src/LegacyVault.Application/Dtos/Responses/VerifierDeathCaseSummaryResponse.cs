namespace LegacyVault.Application.Dtos.Responses;

/// <summary>
/// Một dòng trong danh sách hồ sơ của Verifier (docs/api-contract.md mục 6.12).
/// </summary>
/// <remarks>DATA-02: chỉ có 4 số cuối CCCD của Owner, không có họ tên hay thông tin Executor.</remarks>
public class VerifierDeathCaseSummaryResponse
{
    public Guid Id { get; set; }

    /// <summary>UNDER_REVIEW | SUPPLEMENT_REQUIRED | APPROVED_FOR_HANDOVER | REJECTED.</summary>
    public string Status { get; set; } = string.Empty;

    public int? ReviewVersionNo { get; set; }

    public string? OwnerCitizenIdLast4 { get; set; }

    public DateOnly? DateOfDeath { get; set; }

    /// <summary>Lúc Executor gửi bản đang xét.</summary>
    public DateTimeOffset? SubmittedAt { get; set; }

    public DateTimeOffset AssignedAt { get; set; }

    /// <summary>Hạn xét của phân công đang hiệu lực.</summary>
    public DateTimeOffset DueAt { get; set; }

    public DateTimeOffset? ObjectionEndsAt { get; set; }

    /// <summary>DEATH-10: số cờ của bản đang xét chưa được xem xét.</summary>
    public int UnreviewedFlagCount { get; set; }

    public DateTimeOffset? ClosedAt { get; set; }
}
