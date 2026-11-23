namespace LegacyVault.Application.Dtos.Requests;

/// <summary>Thông tin người thân khai khi nộp hồ sơ báo tin (tệp gửi riêng dạng multipart).</summary>
public class SubmitHandoverReportRequest
{
    public string DeceasedFullName { get; set; } = string.Empty;

    public string DeceasedCitizenId { get; set; } = string.Empty;

    public string? Note { get; set; }

    /// <summary>JSON mảng {fullName, email, phone}; rỗng khi không khai.</summary>
    public string? Recipients { get; set; }
}

/// <summary>Các tệp của hồ sơ báo tin, chia theo loại giấy tờ.</summary>
public class HandoverReportUploads
{
    /// <summary>Bắt buộc ít nhất một tệp.</summary>
    public List<UploadedFile> DeathCertificates { get; set; } = new List<UploadedFile>();

    public List<UploadedFile> Wills { get; set; } = new List<UploadedFile>();

    public List<UploadedFile> Others { get; set; } = new List<UploadedFile>();
}
