namespace LegacyVault.Application.Ekyc;

/// <summary>Kết quả đọc mặt sau giấy tờ từ dịch vụ eKYC.</summary>
public sealed class EkycBackReading
{
    /// <summary>
    /// Số căn cước 12 chữ số đọc từ dòng MRZ của mặt sau; rỗng khi thẻ không có MRZ chứa số này hoặc OCR không đọc được.
    /// Dịch vụ chỉ dùng số này để phát hiện ghép mặt trước và mặt sau của hai thẻ khác nhau.
    /// </summary>
    public string IdNumber { get; set; } = string.Empty;

    /// <summary>
    /// Nơi cư trú đọc từ mặt sau; rỗng khi thẻ không có nhãn nơi cư trú ở mặt sau hoặc OCR không đọc được.
    /// Chỉ để trả về cho người dùng đối chiếu ngay trong phản hồi: không lưu vào DB, không ghi log (BR-88).
    /// </summary>
    public string ResidenceAddress { get; set; } = string.Empty;

    public List<string> Warnings { get; set; } = new List<string>();
}
