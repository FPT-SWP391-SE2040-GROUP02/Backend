using System.ComponentModel.DataAnnotations;

namespace LegacyVault.Api.Models;

/// <summary>
/// Form multipart của các lệnh eKYC gửi một ảnh. Tên field: <c>file</c>.
/// </summary>
/// <remarks>Nằm ở Api vì IFormFile là kiểu của ASP.NET; controller chuyển luồng đọc sang IEkycService.</remarks>
public sealed class EkycImageForm
{
    [Required(ErrorMessage = "Vui lòng chọn ảnh.")]
    public IFormFile? File { get; set; }
}
