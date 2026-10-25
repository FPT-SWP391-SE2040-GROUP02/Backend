namespace LegacyVault.Application.Dtos.Responses;

/// <summary>
/// Chi tiết hồ sơ chứng tử cho Verifier được giao (docs/api-contract.md mục 6.12).
/// </summary>
/// <remarks>
/// DATA-02 (cách hiểu): Verifier được giao là "người cần" (SEC-05) nên bản đang xét có CCCD đầy đủ của Owner;
/// các bản cũ chỉ có 4 số cuối. Không có thông tin Executor, email Owner, khóa lưu trữ, SHA-256, Id người quyết định.
/// </remarks>
public class VerifierDeathCaseResponse
{
    public Guid Id { get; set; }

    public Guid VaultId { get; set; }

    public string Status { get; set; } = string.Empty;

    /// <summary>Gửi lại trong body các thao tác ghi. Mọi thao tác ghi của Verifier làm đổi etag.</summary>
    public string Etag { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? FirstSubmittedAt { get; set; }

    public DateTimeOffset? ClosedAt { get; set; }

    /// <summary>Bản Verifier đang xét (bản Executor gửi gần nhất), giải mã đầy đủ.</summary>
    public VerifierReviewVersionResponse ReviewVersion { get; set; } = new VerifierReviewVersionResponse();

    /// <summary>Các bản đã gửi khác bản đang xét, cũ → mới; không giải mã.</summary>
    public List<VerifierOlderVersionResponse> Versions { get; set; } = new List<VerifierOlderVersionResponse>();

    /// <summary>DEATH-10: cờ của bản đang xét.</summary>
    public List<VerifierFlagResponse> Flags { get; set; } = new List<VerifierFlagResponse>();

    /// <summary>Các quyết định đã có, cũ → mới.</summary>
    public List<VerifierDecisionResponse> Decisions { get; set; } = new List<VerifierDecisionResponse>();

    /// <summary>Thông báo của bản đã báo Owner gần nhất; null khi chưa có.</summary>
    public DeathCaseNoticeResponse? OwnerNotice { get; set; }

    public DateTimeOffset? ObjectionEndsAt { get; set; }

    /// <summary>
    /// DEATH-01: lúc job đánh dấu Executor bổ sung quá hạn 7 ngày; null = chưa quá hạn hoặc đã gửi lại.
    /// Chỉ để hiển thị, không tự từ chối.
    /// </summary>
    public DateTimeOffset? SupplementOverdueAt { get; set; }

    public VerifierAssignmentResponse Assignment { get; set; } = new VerifierAssignmentResponse();

    /// <summary>true khi approveBlockers rỗng.</summary>
    public bool CanApprove { get; set; }

    /// <summary>
    /// Lý do chưa duyệt được (UPPER_SNAKE_CASE): NOT_UNDER_REVIEW, NEWER_VERSION_PENDING, OWNER_NOT_NOTIFIED,
    /// OBJECTION_PERIOD_NOT_OVER, FLAGS_NOT_REVIEWED.
    /// </summary>
    public List<string> ApproveBlockers { get; set; } = new List<string>();

    /// <summary>DEATH-12: hồ sơ bị từ chối trước đó của cùng kho; null khi không có.</summary>
    public VerifierPreviousCaseResponse? PreviousCase { get; set; }

    /// <summary>Chỉ có ở response duyệt lần đầu; còn lại null.</summary>
    public VerifierHandoverResponse? Handover { get; set; }

    /// <summary>
    /// Danh tính Owner đã xác minh qua eKYC, để đối chiếu với bản Executor khai (BR-48 sửa 25/10/2026: Verifier được
    /// giao xem đủ 12 số CCCD). Luôn có object; Verified = false khi Owner chưa gắn danh tính.
    /// </summary>
    public VerifierOwnerIdentityResponse OwnerIdentity { get; set; } = new VerifierOwnerIdentityResponse();
}

/// <summary>Danh tính đã xác minh của Owner và kết quả so với bản đang xét.</summary>
public class VerifierOwnerIdentityResponse
{
    /// <summary>true khi Owner đã gắn danh tính qua eKYC (OWNER_ENROLLMENT đạt).</summary>
    public bool Verified { get; set; }

    /// <summary>12 số CCCD đã xác minh; null khi chưa xác minh.</summary>
    public string? CitizenId { get; set; }

    public string? FullName { get; set; }

    public DateOnly? DateOfBirth { get; set; }

    /// <summary>Lúc gắn danh tính.</summary>
    public DateTimeOffset? VerifiedAt { get; set; }

    /// <summary>CCCD bản đang xét trùng CCCD đã xác minh: true/false; null khi một bên chưa có.</summary>
    public bool? CitizenIdMatches { get; set; }

    /// <summary>Họ tên bản đang xét trùng họ tên đã xác minh (không phân biệt hoa thường, giữ dấu); null khi một bên chưa có.</summary>
    public bool? FullNameMatches { get; set; }

    /// <summary>
    /// Có ảnh mặt trước CCCD lúc eKYC. Xem ảnh qua GET /verifier/death-cases/{caseId}/owner-identity/document-image.
    /// </summary>
    public bool HasDocumentImage { get; set; }

    public DateTimeOffset? DocumentImageCapturedAt { get; set; }
}

public class VerifierReviewVersionResponse
{
    public int VersionNo { get; set; }

    public string? OwnerFullName { get; set; }

    /// <summary>12 số CCCD đầy đủ của Owner.</summary>
    public string? OwnerCitizenId { get; set; }

    public string? OwnerCitizenIdLast4 { get; set; }

    public DateOnly? DateOfDeath { get; set; }

    public string? CertificateNumber { get; set; }

    public DateOnly? IssuedOn { get; set; }

    public string? IssuedBy { get; set; }

    public DateTimeOffset? SubmittedAt { get; set; }

    public List<DeathCaseFileResponse> Files { get; set; } = new List<DeathCaseFileResponse>();
}

public class VerifierOlderVersionResponse
{
    public int VersionNo { get; set; }

    public string? OwnerCitizenIdLast4 { get; set; }

    public DateOnly? DateOfDeath { get; set; }

    public DateTimeOffset? SubmittedAt { get; set; }

    public List<DeathCaseFileResponse> Files { get; set; } = new List<DeathCaseFileResponse>();
}

public class VerifierFlagResponse
{
    public Guid Id { get; set; }

    /// <summary>CHECKIN_AFTER_DEATH | IDENTITY_MISMATCH | CERTIFICATE_UNVERIFIED.</summary>
    public string Kind { get; set; } = string.Empty;

    public string Detail { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>null khi chưa xem xét.</summary>
    public DateTimeOffset? ReviewedAt { get; set; }

    public string? ReviewNote { get; set; }
}

public class VerifierDecisionResponse
{
    /// <summary>SUPPLEMENT_REQUESTED | REJECTED | APPROVED.</summary>
    public string Kind { get; set; } = string.Empty;

    public int VersionNo { get; set; }

    public string Reason { get; set; } = string.Empty;

    /// <summary>Chỉ có với SUPPLEMENT_REQUESTED: mã viết hoa như missingRequirements.</summary>
    public List<string> RequestedParts { get; set; } = new List<string>();

    public DateTimeOffset DecidedAt { get; set; }
}

public class VerifierAssignmentResponse
{
    public DateTimeOffset AssignedAt { get; set; }

    public DateTimeOffset DueAt { get; set; }
}

public class VerifierPreviousCaseResponse
{
    public Guid Id { get; set; }

    public DateTimeOffset? ClosedAt { get; set; }

    public string? RejectReason { get; set; }
}

/// <summary>Kết quả mở bàn giao khi duyệt lần đầu.</summary>
public class VerifierHandoverResponse
{
    public int TurnCount { get; set; }

    public int InvitationsSent { get; set; }

    public int InvitationsFailed { get; set; }
}
