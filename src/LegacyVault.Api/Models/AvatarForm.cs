using System.ComponentModel.DataAnnotations;

namespace LegacyVault.Api.Models;

/// <summary>Form multipart của PUT /api/v1/me/avatar. Tên field: <c>file</c>.</summary>
public sealed class AvatarForm
{
    [Required(ErrorMessage = "Vui lòng chọn ảnh.")]
    public IFormFile? File { get; set; }
}
