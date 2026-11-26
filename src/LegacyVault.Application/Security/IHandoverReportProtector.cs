namespace LegacyVault.Application.Security;

/// <summary>Phần dữ liệu cá nhân của hồ sơ báo tin, dạng đã giải mã (chỉ tồn tại trong RAM).</summary>
public class HandoverReportData
{
    public string DeceasedFullName { get; set; } = string.Empty;

    /// <summary>Đủ 12 chữ số.</summary>
    public string DeceasedCitizenId { get; set; } = string.Empty;

    /// <summary>Người nhận do người nộp khai, chỉ để luật sư điền sẵn rồi kiểm lại.</summary>
    public List<SuggestedRecipient> Recipients { get; set; } = new List<SuggestedRecipient>();
}

/// <summary>Một người nhận do người nộp khai.</summary>
public class SuggestedRecipient
{
    public string FullName { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;

    public string Phone { get; set; } = string.Empty;
}

/// <summary>Mã hóa/giải mã dữ liệu cá nhân của hồ sơ báo tin. Cài đặt ở Infrastructure.</summary>
public interface IHandoverReportProtector
{
    string Protect(HandoverReportData data);

    /// <summary>Dữ liệu bị sửa hoặc thiếu KEK → CryptographicException.</summary>
    HandoverReportData Unprotect(string value);
}
