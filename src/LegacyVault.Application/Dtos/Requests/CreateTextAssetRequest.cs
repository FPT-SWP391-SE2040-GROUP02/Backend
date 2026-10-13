using System.ComponentModel.DataAnnotations;

namespace LegacyVault.Application.Dtos.Requests;

/// <summary>Body của POST /api/v1/vaults/{vaultId}/assets/text: tài sản dạng chữ (thông tin tài khoản, ghi chú...).</summary>
/// <remarks>
/// Content là nội dung bí mật, lưu như nội dung tài sản (không nằm trong bảng Assets).
/// Tên/mô tả không mã hóa: không ghi bí mật vào đây (ASSET-02).
/// </remarks>
public class CreateTextAssetRequest
{
    [Required(ErrorMessage = "Tên tài sản là bắt buộc.")]
    [MaxLength(200, ErrorMessage = "Tên tài sản tối đa 200 ký tự.")]
    public string Name { get; set; } = string.Empty;

    [MaxLength(1000, ErrorMessage = "Mô tả tối đa 1000 ký tự.")]
    public string? Description { get; set; }

    [Required(ErrorMessage = "Nội dung là bắt buộc.")]
    [MaxLength(100_000, ErrorMessage = "Nội dung tối đa 100.000 ký tự.")]
    public string Content { get; set; } = string.Empty;
}
