using System.ComponentModel.DataAnnotations;
using LegacyVault.Application.Dtos.Requests;

namespace LegacyVault.Api.Controllers;

/// <summary>Biểu mẫu nộp hồ sơ người nhận: thông tin khai kèm hai ảnh căn cước.</summary>
public sealed class SubmitHandoverSubmissionForm : SubmitHandoverSubmissionRequest
{
    [Required(ErrorMessage = "Vui lòng chọn ảnh căn cước mặt trước.")]
    public IFormFile? FrontImage { get; set; }

    [Required(ErrorMessage = "Vui lòng chọn ảnh căn cước mặt sau.")]
    public IFormFile? BackImage { get; set; }
}
