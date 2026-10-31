namespace LegacyVault.Application.Dtos.Responses;

/// <summary>
/// DEATH-14: phiếu chữ ký số quyết định cuối của hồ sơ chứng tử và kết quả kiểm tra lại (docs/api-contract.md mục 6.12).
/// </summary>
/// <remarks>
/// Chữ ký số nội bộ hệ thống, không phải chữ ký số có giá trị pháp lý của CA công cộng.
/// Không có khóa riêng, DEK hay mật khẩu.
/// </remarks>
public class DecisionSignatureResponse
{
    /// <summary>VALID | INVALID | NOT_SIGNED | NO_FINAL_DECISION.</summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>APPROVED | REJECTED; null khi chưa có quyết định cuối.</summary>
    public string? DecisionKind { get; set; }

    /// <summary>Bản hồ sơ được xét lúc quyết định.</summary>
    public int? VersionNo { get; set; }

    public DateTimeOffset? DecidedAt { get; set; }

    public Guid? SignedByUserId { get; set; }

    public string? SignedByFullName { get; set; }

    /// <summary>"RSA-2048-PSS-SHA256"; null khi chưa ký hoặc không còn khóa.</summary>
    public string? Algorithm { get; set; }

    /// <summary>16 ký tự hex đầu của SHA-256 khóa công khai, nhóm 4 (ví dụ 7F3A-91C2-04BE-55D1).</summary>
    public string? KeyFingerprint { get; set; }

    public string? SignatureBase64 { get; set; }
}
