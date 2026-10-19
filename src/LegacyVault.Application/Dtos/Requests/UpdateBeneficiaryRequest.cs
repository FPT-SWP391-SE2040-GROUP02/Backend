using System.ComponentModel.DataAnnotations;

namespace LegacyVault.Application.Dtos.Requests;

/// <summary>
/// Body của PUT /api/v1/vaults/{vaultId}/beneficiaries/{beneficiaryId}: sửa hồ sơ người nhận.
/// </summary>
/// <remarks>
/// Không có CitizenId: không cho đổi số CCCD; muốn đổi thì xóa hồ sơ và tạo lại.
/// FullName/DateOfBirth null = giữ nguyên. Email/Phone null = giữ nguyên, "" = xóa.
/// </remarks>
public class UpdateBeneficiaryRequest
{
    /// <summary>Giá trị Etag lấy từ BeneficiaryResponse lúc FE đọc hồ sơ.</summary>
    [Required(ErrorMessage = "Thiếu etag của dữ liệu đang sửa.")]
    [MaxLength(64)]
    public string Etag { get; set; } = string.Empty;

    [MaxLength(200, ErrorMessage = "Họ tên tối đa 200 ký tự.")]
    public string? FullName { get; set; }

    public DateOnly? DateOfBirth { get; set; }

    [MaxLength(254, ErrorMessage = "Email tối đa 254 ký tự.")]
    public string? Email { get; set; }

    [MaxLength(20, ErrorMessage = "Số điện thoại tối đa 20 ký tự.")]
    public string? Phone { get; set; }
}
