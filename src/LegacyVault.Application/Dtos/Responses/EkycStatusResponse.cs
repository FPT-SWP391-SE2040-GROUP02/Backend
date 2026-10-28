namespace LegacyVault.Application.Dtos.Responses;

/// <summary>Kết quả eKYC hiện hành của người dùng (GET /api/v1/ekyc/me, POST /api/v1/ekyc/face-match).</summary>
public class EkycStatusResponse
{
    /// <summary>NOT_STARTED, PENDING, PASSED hoặc FAILED.</summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>OWNER_ENROLLMENT, BENEFICIARY_CLAIM hoặc OWNER_ALIVE; null khi chưa làm.</summary>
    public string? Purpose { get; set; }

    /// <summary>NOT_CHECKED, MATCHED, NEEDS_REVIEW hoặc WRONG_PERSON.</summary>
    public string IdentityMatch { get; set; } = "NOT_CHECKED";

    /// <summary>true khi mặt sau cùng thẻ với mặt trước (EKYC-04).</summary>
    public bool BackSideChecked { get; set; }

    public Guid? VerificationId { get; set; }

    /// <summary>4 số cuối CCCD; chuỗi rỗng khi chưa đọc được.</summary>
    public string CitizenIdLast4 { get; set; } = string.Empty;

    /// <summary>Điểm cosine của lần so khớp; không phải phần trăm.</summary>
    public double? FaceMatchScore { get; set; }

    /// <summary>Ngưỡng server dùng để quyết định PASSED/FAILED.</summary>
    public double MatchThreshold { get; set; }

    public bool? SpoofCheckPassed { get; set; }

    public DateTimeOffset? CreatedAt { get; set; }

    public DateTimeOffset? CompletedAt { get; set; }

    /// <summary>Ảnh cắt khuôn mặt tham chiếu từ CCCD (data URL); chỉ trả sau lần so khớp.</summary>
    public string? ReferenceCrop { get; set; }

    /// <summary>Ảnh cắt khuôn mặt từ selfie (data URL); chỉ trả sau lần so khớp.</summary>
    public string? SelfieCrop { get; set; }
}
