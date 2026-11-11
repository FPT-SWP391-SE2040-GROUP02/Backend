namespace LegacyVault.Application.Dtos.Responses;

/// <summary>Tình trạng ảnh thẻ công chứng viên của Người xét duyệt đang đăng nhập.</summary>
public class NotaryCardStatusResponse
{
    /// <summary>true khi đã có ảnh thẻ. Chưa có thì không duyệt được hồ sơ và không gửi được biên bản.</summary>
    public bool HasCard { get; set; }

    /// <summary>Thời điểm nộp ảnh gần nhất (Admin hoặc chính Người xét duyệt); null khi chưa có.</summary>
    public DateTimeOffset? UploadedAt { get; set; }
}
