namespace LegacyVault.Domain.Enums;

/// <summary>
/// Kết quả đối chiếu giấy tờ vừa đọc với danh tính đã gắn của Owner (EKYC-24). Chỉ tính tại chỗ, không lưu DB.
/// </summary>
public enum EkycIdentityMatch
{
    /// <summary>Số CCCD trùng chính xác, họ tên và ngày sinh trùng.</summary>
    Matched = 0,
    /// <summary>OCR không chắc chắn, hoặc CCCD trùng mà tên/ngày sinh khác: phải chụp lại.</summary>
    NeedsReview = 1,
    /// <summary>Số CCCD khác rõ ràng: sai người.</summary>
    WrongPerson = 2,
}
