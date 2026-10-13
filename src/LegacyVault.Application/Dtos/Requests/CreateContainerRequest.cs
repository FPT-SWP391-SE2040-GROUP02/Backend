using System.ComponentModel.DataAnnotations;

namespace LegacyVault.Application.Dtos.Requests;

/// <summary>Body của POST /api/v1/vaults/{vaultId}/containers. Gói mới luôn rỗng và ở DRAFT.</summary>
/// <remarks>Tên/mô tả không mã hóa: không ghi mật khẩu, seed phrase vào đây (ASSET-02).</remarks>
public class CreateContainerRequest
{
    [Required(ErrorMessage = "Tên gói là bắt buộc.")]
    [MaxLength(200, ErrorMessage = "Tên gói tối đa 200 ký tự.")]
    public string Name { get; set; } = string.Empty;

    [MaxLength(1000, ErrorMessage = "Mô tả tối đa 1000 ký tự.")]
    public string? Description { get; set; }
}
