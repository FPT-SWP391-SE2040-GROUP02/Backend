namespace LegacyVault.Application.Dtos.Responses;

/// <summary>
/// Thông tin đọc được từ mặt trước giấy tờ (POST /api/v1/ekyc/document).
/// Họ tên, ngày sinh, địa chỉ chỉ trả lần này, server không lưu.
/// </summary>
public class EkycDocumentResponse
{
    /// <summary>Id lượt eKYC; null khi chưa tìm được khuôn mặt trên thẻ (CanMatchFace = false).</summary>
    public Guid? VerificationId { get; set; }

    public string IdNumber { get; set; } = string.Empty;

    public string FullName { get; set; } = string.Empty;

    public string DateOfBirth { get; set; } = string.Empty;

    public string ResidenceAddress { get; set; } = string.Empty;

    /// <summary>
    /// NOT_CHECKED, MATCHED, NEEDS_REVIEW hoặc WRONG_PERSON. Chỉ khác NOT_CHECKED khi mục đích là BENEFICIARY_CLAIM.
    /// Cố ý không trả số CCCD, họ tên hay ngày sinh mong đợi (CLAIM-05).
    /// </summary>
    public string IdentityMatch { get; set; } = "NOT_CHECKED";

    /// <summary>true khi đã giữ được khuôn mặt trên thẻ, FE có thể gửi selfie để so khớp.</summary>
    public bool CanMatchFace { get; set; }

    /// <summary>Hạn gửi selfie; null khi CanMatchFace = false.</summary>
    public DateTimeOffset? ExpiresAt { get; set; }

    public List<string> Warnings { get; set; } = new List<string>();
}
