namespace LegacyVault.Application.Dtos.Responses;

/// <summary>Kết quả eKYC hiện hành của người dùng (GET /api/v1/ekyc/me, POST /api/v1/ekyc/face-match).</summary>
public class EkycStatusResponse
{
    /// <summary>NOT_STARTED, PENDING, PASSED hoặc FAILED.</summary>
    public string Status { get; set; } = string.Empty;

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
}
