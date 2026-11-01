using System.ComponentModel.DataAnnotations;

namespace LegacyVault.Application.Dtos.Requests;

/// <summary>
/// Body của PUT /api/v1/vaults/{vaultId}: sửa tên, mô tả kho và hướng dẫn cho Executor.
/// </summary>
/// <remarks>
/// Dùng PUT và đặt ETag trong body (không dùng header If-Match) để khớp baseService.update(id, data) của FE.
/// null = giữ nguyên; description "" = xóa mô tả. Không ghi bí mật vào đây (ASSET-02).
/// </remarks>
public class UpdateVaultRequest
{
    /// <summary>Giá trị Etag lấy từ VaultResponse lúc FE đọc kho; chống ghi đè thay đổi của tab khác.</summary>
    [Required(ErrorMessage = "Thiếu etag của dữ liệu đang sửa.")]
    [MaxLength(64)]
    public string Etag { get; set; } = string.Empty;

    [MaxLength(200, ErrorMessage = "Tên kho tối đa 200 ký tự.")]
    public string? Name { get; set; }

    [MaxLength(1000, ErrorMessage = "Mô tả tối đa 1000 ký tự.")]
    public string? Description { get; set; }

    /// <summary>Hướng dẫn cho Executor; null = giữ nguyên, "" = xóa. Executor đọc được trước khi hồ sơ chứng tử được duyệt.</summary>
    [MaxLength(2000, ErrorMessage = "Hướng dẫn cho Executor tối đa 2000 ký tự.")]
    public string? ExecutorNote { get; set; }
}
