using System.ComponentModel.DataAnnotations;

namespace LegacyVault.Application.Dtos.Requests;

/// <summary>
/// Body (không bắt buộc) của POST /api/v1/vaults — nút "Tạo kho của tôi" (§5.2 bước 1).
/// </summary>
/// <remarks>
/// Body rỗng thì kho tên "Kho của tôi". Owner đã có kho thì tên/mô tả gửi lên bị bỏ qua, trả lại kho cũ.
/// Không có trường ownerId: chủ kho luôn lấy từ ICurrentUser (SETUP-12, SEC-04).
/// </remarks>
public class CreateVaultRequest
{
    [MaxLength(200, ErrorMessage = "Tên kho tối đa 200 ký tự.")]
    public string? Name { get; set; }

    [MaxLength(1000, ErrorMessage = "Mô tả tối đa 1000 ký tự.")]
    public string? Description { get; set; }
}
