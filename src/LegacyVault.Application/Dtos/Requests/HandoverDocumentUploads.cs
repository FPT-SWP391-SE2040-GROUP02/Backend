namespace LegacyVault.Application.Dtos.Requests;

/// <summary>
/// Các tệp nộp lần đầu, chia theo loại giấy tờ. Dùng chung cho hồ sơ báo tin và yêu cầu nhận tài sản.
/// </summary>
public class HandoverDocumentUploads
{
    /// <summary>Giấy chứng tử; bắt buộc ít nhất một tệp.</summary>
    public List<UploadedFile> DeathCertificates { get; set; } = new List<UploadedFile>();

    /// <summary>Di chúc; bắt buộc ít nhất một tệp.</summary>
    public List<UploadedFile> Wills { get; set; } = new List<UploadedFile>();

    /// <summary>Giấy tờ khác; không bắt buộc.</summary>
    public List<UploadedFile> Others { get; set; } = new List<UploadedFile>();
}
