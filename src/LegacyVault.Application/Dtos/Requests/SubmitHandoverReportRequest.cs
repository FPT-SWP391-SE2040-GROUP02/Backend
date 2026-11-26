namespace LegacyVault.Application.Dtos.Requests;

/// <summary>Thông tin người thân khai khi nộp hồ sơ báo tin (tệp gửi riêng dạng multipart).</summary>
public class SubmitHandoverReportRequest
{
    public string DeceasedFullName { get; set; } = string.Empty;

    public string DeceasedCitizenId { get; set; } = string.Empty;

    /// <summary>Phải true: người nộp cam đoan giấy tờ là thật.</summary>
    public bool Attested { get; set; }

    public string? Note { get; set; }

    /// <summary>JSON mảng {fullName, email, phone}; rỗng khi không khai.</summary>
    public string? Recipients { get; set; }
}
