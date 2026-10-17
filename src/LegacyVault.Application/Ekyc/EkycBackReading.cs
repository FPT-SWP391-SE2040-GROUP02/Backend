namespace LegacyVault.Application.Ekyc;

/// <summary>Kết quả đọc mặt sau giấy tờ từ dịch vụ eKYC.</summary>
public sealed class EkycBackReading
{
    /// <summary>
    /// Số căn cước 12 chữ số đọc từ dòng MRZ của mặt sau; rỗng khi thẻ không có MRZ chứa số này hoặc OCR không đọc được.
    /// Dịch vụ chỉ dùng số này để phát hiện ghép mặt trước và mặt sau của hai thẻ khác nhau.
    /// </summary>
    public string IdNumber { get; set; } = string.Empty;

    public List<string> Warnings { get; set; } = new List<string>();
}
