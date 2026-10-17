namespace LegacyVault.Domain.Enums;

/// <summary>
/// Kết quả đối chiếu giấy tờ với người được chỉ định trong snapshot (SRS mục 4.4, MATCH-01..05).
/// </summary>
public enum EkycIdentityMatch
{
    /// <summary>Không có gì để đối chiếu (mục đích không phải BeneficiaryClaim).</summary>
    NotChecked = 0,
    /// <summary>Số CCCD trùng chính xác, họ tên và ngày sinh trùng (MATCH-01..03).</summary>
    Matched = 1,
    /// <summary>OCR không chắc chắn, hoặc CCCD trùng mà tên/ngày sinh khác: giữ kết quả gốc để Executor xem (MATCH-04, MATCH-06).</summary>
    NeedsReview = 2,
    /// <summary>Số CCCD khác rõ ràng: sai người (MATCH-05).</summary>
    WrongPerson = 3,
}
