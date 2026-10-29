using System.ComponentModel.DataAnnotations;

namespace LegacyVault.Application.Dtos.Requests;

/// <summary>Body của POST /api/v1/vaults/{vaultId}/beneficiaries: tạo hồ sơ người nhận cấp kho.</summary>
/// <remarks>
/// Cần ít nhất email hoặc số điện thoại (§5.2 bước 7). Email/Phone "" hoặc chỉ khoảng trắng = không có;
/// định dạng email/số điện thoại do service kiểm.
/// </remarks>
public class CreateBeneficiaryRequest
{
    /// <summary>Số CCCD 12 chữ số (được phép có khoảng trắng, service bỏ đi). Không bao giờ được trả lại cho FE.</summary>
    [Required(ErrorMessage = "Số CCCD là bắt buộc.")]
    [MaxLength(32, ErrorMessage = "Số CCCD không hợp lệ.")]
    public string CitizenId { get; set; } = string.Empty;

    [Required(ErrorMessage = "Họ tên là bắt buộc.")]
    [StringLength(200, MinimumLength = 2, ErrorMessage = "Họ và tên phải từ 2 đến 200 ký tự.")]
    [RegularExpression(@"^[\p{L}\s]+$", ErrorMessage = "Họ và tên chỉ được chứa chữ cái và khoảng trắng.")]
    public string FullName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Ngày sinh là bắt buộc.")]
    public DateOnly? DateOfBirth { get; set; }

    [Required(ErrorMessage = "Email là bắt buộc.")]
    [EmailAddress(ErrorMessage = "Email không hợp lệ.")]
    [MaxLength(254, ErrorMessage = "Email tối đa 254 ký tự.")]
    public string Email { get; set; } = string.Empty;

    [MaxLength(20, ErrorMessage = "Số điện thoại tối đa 20 ký tự.")]
    public string? Phone { get; set; }
}
