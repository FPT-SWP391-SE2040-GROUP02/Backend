namespace LegacyVault.Application.Dtos.Responses;

/// <summary>Phần 2 của biên bản, do người nhận điền và ký.</summary>
public class HandoverDocumentRecipientPart
{
    public string FullName { get; set; } = string.Empty;

    public int? BirthYear { get; set; }

    /// <summary>Số Căn cước/CCCD 12 số của người nhận (giải mã trong RAM từ hồ sơ người được chỉ định để hiển thị trên biên bản).</summary>
    public string CitizenId { get; set; } = string.Empty;

    public string CitizenIdLast4 { get; set; } = string.Empty;

    /// <summary>Số căn cước đầy đủ người nhận đã gõ lúc ký; chỉ Người xét duyệt thấy, rỗng khi không có.</summary>
    public string ClaimedCitizenId { get; set; } = string.Empty;

    /// <summary>Ngày sinh đầy đủ người nhận đã gõ lúc ký; chỉ Người xét duyệt thấy, null khi không có.</summary>
    public DateOnly? ClaimedDateOfBirth { get; set; }

    /// <summary>Họ tên Chủ kho đã khai cho người nhận; chỉ Người xét duyệt thấy, rỗng khi không có.</summary>
    public string DeclaredFullName { get; set; } = string.Empty;

    /// <summary>Ngày sinh Chủ kho đã khai cho người nhận; chỉ Người xét duyệt thấy, null khi không có.</summary>
    public DateOnly? DeclaredDateOfBirth { get; set; }

    /// <summary>Nơi thường trú người nhận khai; rỗng khi chưa ký.</summary>
    public string ResidenceAddress { get; set; } = string.Empty;

    public DateTimeOffset? SignedAt { get; set; }
}
