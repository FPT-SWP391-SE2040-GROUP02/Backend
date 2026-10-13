using System.ComponentModel.DataAnnotations;

namespace LegacyVault.Api.Models;

/// <summary>
/// Form multipart của POST /api/v1/vaults/{vaultId}/assets. Tên field: <c>file</c>, <c>name</c>, <c>description</c>.
/// </summary>
/// <remarks>Nằm ở Api vì IFormFile là kiểu của ASP.NET; controller chuyển sang CreateFileAssetRequest.</remarks>
public sealed class CreateFileAssetForm
{
    [Required(ErrorMessage = "Vui lòng chọn tệp.")]
    public IFormFile? File { get; set; }

    /// <summary>Không gửi thì lấy theo tên tệp.</summary>
    [MaxLength(200, ErrorMessage = "Tên tài sản tối đa 200 ký tự.")]
    public string? Name { get; set; }

    [MaxLength(1000, ErrorMessage = "Mô tả tối đa 1000 ký tự.")]
    public string? Description { get; set; }
}
