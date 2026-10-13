using System.ComponentModel.DataAnnotations;

namespace LegacyVault.Application.Dtos.Requests;

/// <summary>
/// Body của PUT /api/v1/vaults/{vaultId}/containers/{containerId}: đổi tên/mô tả.
/// </summary>
/// <remarks>Giống UpdateVaultRequest: ETag trong body để khớp baseService.update của FE; null = giữ nguyên.</remarks>
public class UpdateContainerRequest
{
    /// <summary>Giá trị Etag lấy từ ContainerResponse lúc FE đọc gói.</summary>
    [Required(ErrorMessage = "Thiếu etag của dữ liệu đang sửa.")]
    [MaxLength(64)]
    public string Etag { get; set; } = string.Empty;

    [MaxLength(200, ErrorMessage = "Tên gói tối đa 200 ký tự.")]
    public string? Name { get; set; }

    [MaxLength(1000, ErrorMessage = "Mô tả tối đa 1000 ký tự.")]
    public string? Description { get; set; }
}
