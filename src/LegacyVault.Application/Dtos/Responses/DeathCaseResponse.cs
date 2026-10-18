namespace LegacyVault.Application.Dtos.Responses;

/// <summary>
/// Hồ sơ chứng tử trả cho Executor (docs/api-contract.md mục 6.10).
/// </summary>
/// <remarks>
/// DATA-02: KHÔNG có số CCCD đầy đủ, chỉ có 4 số cuối. SEC-07/ROLE-03: không có thông tin Verifier được giao.
/// </remarks>
public class DeathCaseResponse
{
    public Guid Id { get; set; }

    public Guid VaultId { get; set; }

    /// <summary>DRAFT | UNDER_REVIEW | SUPPLEMENT_REQUIRED | APPROVED_FOR_HANDOVER | REJECTED | SUSPENDED.</summary>
    public string Status { get; set; } = string.Empty;

    public DeathCaseVersionResponse CurrentVersion { get; set; } = new DeathCaseVersionResponse();

    /// <summary>Bản mà Verifier đang xét (bản gửi gần nhất); null khi chưa gửi lần nào.</summary>
    public int? ReviewVersionNo { get; set; }

    /// <summary>
    /// DEATH-01: các phần còn thiếu của bản hiện tại: OWNER_FULL_NAME, OWNER_CITIZEN_ID, DATE_OF_DEATH,
    /// CERTIFICATE_NUMBER, ISSUED_ON, ISSUED_BY, FILES. Rỗng khi đủ.
    /// </summary>
    public List<string> MissingRequirements { get; set; } = new List<string>();

    /// <summary>
    /// DEATH-01: yêu cầu bổ sung gần nhất của Verifier khi hồ sơ đang SUPPLEMENT_REQUIRED; trạng thái khác thì null.
    /// Không có thông tin Verifier.
    /// </summary>
    public DeathCaseSupplementRequestResponse? SupplementRequest { get; set; }

    /// <summary>
    /// DEATH-12: lý do Verifier từ chối khi hồ sơ REJECTED (Executor là người phải mở hồ sơ mới); trạng thái khác thì null.
    /// Không có thông tin Verifier.
    /// </summary>
    public string? RejectReason { get; set; }

    /// <summary>Thông báo gần nhất cho Owner (DEATH-08); null khi chưa gửi hồ sơ lần nào.</summary>
    public DeathCaseNoticeResponse? OwnerNotice { get; set; }

    /// <summary>DEATH-08: hết 72 giờ phản đối; null khi đồng hồ chưa chạy (thông báo chưa được giao).</summary>
    public DateTimeOffset? ObjectionEndsAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? UpdatedAt { get; set; }

    public string Etag { get; set; } = string.Empty;
}

/// <summary>Phiên bản hiện tại (bản nháp hoặc bản mới gửi) của hồ sơ.</summary>
public class DeathCaseVersionResponse
{
    public int VersionNo { get; set; }

    /// <summary>true khi Executor chưa xác nhận gửi bản này (còn sửa được).</summary>
    public bool IsDraft { get; set; }

    public string? OwnerFullName { get; set; }

    /// <summary>DATA-02: chỉ 4 số cuối CCCD của Owner.</summary>
    public string? OwnerCitizenIdLast4 { get; set; }

    public DateOnly? DateOfDeath { get; set; }

    public string? CertificateNumber { get; set; }

    public DateOnly? IssuedOn { get; set; }

    public string? IssuedBy { get; set; }

    public List<DeathCaseFileResponse> Files { get; set; } = new List<DeathCaseFileResponse>();

    public DateTimeOffset? SubmittedAt { get; set; }
}

/// <summary>Một trang giấy chứng tử (chỉ metadata, không có nội dung, khóa lưu trữ hay SHA-256).</summary>
public class DeathCaseFileResponse
{
    public Guid Id { get; set; }

    public int Position { get; set; }

    public string FileName { get; set; } = string.Empty;

    public string ContentType { get; set; } = string.Empty;

    public long SizeBytes { get; set; }

    public DateTimeOffset UploadedAt { get; set; }
}

/// <summary>Trạng thái thông báo cho Owner.</summary>
public class DeathCaseNoticeResponse
{
    /// <summary>PENDING | DELIVERED | FAILED | CANCELLED (hồ sơ bị từ chối trước khi gửi được thư).</summary>
    public string Status { get; set; } = string.Empty;

    public DateTimeOffset? DeliveredAt { get; set; }
}

/// <summary>DEATH-01: Verifier yêu cầu Executor bổ sung những gì.</summary>
public class DeathCaseSupplementRequestResponse
{
    /// <summary>Bản hồ sơ mà Verifier đã xét khi yêu cầu.</summary>
    public int VersionNo { get; set; }

    public string Reason { get; set; } = string.Empty;

    /// <summary>Mã viết hoa như missingRequirements: OWNER_FULL_NAME, ..., FILES.</summary>
    public List<string> RequestedParts { get; set; } = new List<string>();

    public DateTimeOffset RequestedAt { get; set; }

    /// <summary>Hạn bổ sung; null với dữ liệu cũ chưa có hạn.</summary>
    public DateTimeOffset? DueAt { get; set; }

    /// <summary>true khi đã quá hạn bổ sung; Verifier sẽ quyết định bước tiếp theo.</summary>
    public bool Overdue { get; set; }
}
