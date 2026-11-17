namespace LegacyVault.Application.Dtos.Responses;

/// <summary>Tình trạng ảnh thẻ công chứng viên của chính người đang đăng nhập (không chứa nội dung ảnh).</summary>
public sealed class NotaryCardStatusResponse
{
    public bool HasCard { get; set; }

    public DateTimeOffset? UploadedAt { get; set; }
}
