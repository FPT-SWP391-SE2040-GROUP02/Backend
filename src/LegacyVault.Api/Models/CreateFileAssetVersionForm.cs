using System.ComponentModel.DataAnnotations;

namespace LegacyVault.Api.Models;

/// <summary>
/// Form multipart của POST /api/v1/vaults/{vaultId}/assets/{assetId}/versions. Tên field: <c>file</c>, <c>etag</c>.
/// </summary>
public sealed class CreateFileAssetVersionForm
{
    [Required(ErrorMessage = "Vui lòng chọn tệp.")]
    public IFormFile? File { get; set; }

    [Required(ErrorMessage = "Thiếu etag.")]
    public string? Etag { get; set; }
}
