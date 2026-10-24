using System.ComponentModel.DataAnnotations;

namespace LegacyVault.Application.Dtos.Requests;

/// <summary>
/// Body của PUT /api/v1/vaults/{vaultId}/assets/{assetId}: đổi tên và/hoặc mô tả (ASSET-04).
/// Không gửi (null) = giữ nguyên; description "" = xóa mô tả. Tên/mô tả không mã hóa, không ghi bí mật vào đây.
/// </summary>
public class UpdateAssetRequest
{
    [MaxLength(200, ErrorMessage = "Tên tài sản tối đa 200 ký tự.")]
    public string? Name { get; set; }

    [MaxLength(1000, ErrorMessage = "Mô tả tối đa 1000 ký tự.")]
    public string? Description { get; set; }

    /// <summary>etag của AssetResponse đã đọc; lệch thì 409 EDIT_CONFLICT.</summary>
    [Required(ErrorMessage = "Thiếu etag.")]
    public string Etag { get; set; } = string.Empty;
}
