namespace LegacyVault.Application.Repositories;

/// <summary>Hai mốc ký trên biên bản của một lượt, để màn hình Người thực thi biết biên bản đang chờ ai. Chỉ có giờ, không có nội dung.</summary>
public sealed class HandoverDocumentTimes
{
    /// <summary>Lúc người nhận ký biên bản (đồng ý hoặc từ chối); null khi chưa ký.</summary>
    public DateTimeOffset? RecipientSignedAt { get; set; }

    /// <summary>Lúc Người xét duyệt ký biên bản; null khi chưa ký.</summary>
    public DateTimeOffset? VerifierSignedAt { get; set; }
}
