namespace LegacyVault.Application.Ekyc;

/// <summary>Kết quả đọc mặt trước giấy tờ từ dịch vụ eKYC.</summary>
public sealed class EkycDocumentReading
{
    public string IdNumber { get; set; } = string.Empty;

    public string FullName { get; set; } = string.Empty;

    public string DateOfBirth { get; set; } = string.Empty;

    public string ResidenceAddress { get; set; } = string.Empty;

    /// <summary>Mã tham chiếu khuôn mặt trên giấy tờ; null khi dịch vụ chưa tìm được đúng một khuôn mặt.</summary>
    public string? DocumentToken { get; set; }

    public List<string> Warnings { get; set; } = new List<string>();
}
