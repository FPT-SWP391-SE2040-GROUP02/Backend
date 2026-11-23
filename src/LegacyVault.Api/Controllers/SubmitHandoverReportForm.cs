using LegacyVault.Application.Dtos.Requests;

namespace LegacyVault.Api.Controllers;

/// <summary>Biểu mẫu nộp hồ sơ báo tin: thông tin khai kèm tệp theo loại giấy tờ.</summary>
public sealed class SubmitHandoverReportForm : SubmitHandoverReportRequest
{
    public List<IFormFile>? DeathCertificates { get; set; }

    public List<IFormFile>? Wills { get; set; }

    public List<IFormFile>? Others { get; set; }
}
