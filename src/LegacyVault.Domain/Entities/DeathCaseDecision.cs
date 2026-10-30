using LegacyVault.Domain.Enums;

namespace LegacyVault.Domain.Entities;

/// <summary>
/// Một quyết định của Verifier trên hồ sơ chứng tử (bảng DeathCaseDecisions), SRS §6.2 F2B.
/// </summary>
/// <remarks>
/// - Chỉ thêm dòng, không sửa (DEATH-03): mỗi quyết định ghi người quyết, phân công và bản hồ sơ được xét.
/// - AC-71: mỗi hồ sơ tối đa một quyết định cuối (Rejected hoặc Approved), giữ bằng chỉ mục unique có lọc;
///   yêu cầu bổ sung thì được nhiều lần.
/// - Quyết định cuối chỉ đóng hồ sơ (ClosedAt), không kết thúc phân công.
/// </remarks>
public class DeathCaseDecision
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid CaseId { get; set; }

    /// <summary>DEATH-04, STATE-03: số phiên bản (ReviewVersionNo) lúc Verifier quyết định.</summary>
    public int VersionNo { get; set; }

    public DeathCaseDecisionKind Kind { get; set; }

    /// <summary>Lý do Verifier nhập (≤ 1000 ký tự), bắt buộc cho cả 3 loại.</summary>
    public string Reason { get; set; } = string.Empty;

    /// <summary>
    /// Chỉ có với SupplementRequested: các mã phần cần bổ sung đã chuẩn hóa, nối bằng dấu phẩy
    /// (ví dụ "DATE_OF_DEATH,FILES"); tối đa 256 ký tự.
    /// </summary>
    public string? RequestedParts { get; set; }

    public Guid VerifierUserId { get; set; }

    /// <summary>Phân công đang hiệu lực lúc Verifier quyết định.</summary>
    public Guid AssignmentId { get; set; }

    public DateTimeOffset DecidedAt { get; set; }

    /// <summary>Hạn Executor bổ sung (yêu cầu bổ sung: DecidedAt + 7 ngày); null với từ chối và duyệt.</summary>
    public DateTimeOffset? DueAt { get; set; }

    /// <summary>DEATH-14: khóa ký của Verifier đã ký quyết định; null với yêu cầu bổ sung và quyết định cũ chưa ký.</summary>
    public Guid? SigningKeyId { get; set; }

    /// <summary>DEATH-14: chữ ký RSA-PSS (256 byte) trên chuỗi dựng bởi DecisionSignatureRules.BuildPayload.</summary>
    public byte[]? Signature { get; set; }
}
