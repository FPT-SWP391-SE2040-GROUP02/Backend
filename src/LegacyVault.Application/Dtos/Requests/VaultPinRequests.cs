using System.ComponentModel.DataAnnotations;

namespace LegacyVault.Application.Dtos.Requests;

/// <summary>Body của POST api/v1/vaults/{vaultId}/pin: đặt PIN lần đầu.</summary>
public class SetVaultPinRequest
{
    [Required(ErrorMessage = "Vui lòng nhập mã PIN.")]
    [RegularExpression(@"^\d{6}$", ErrorMessage = "Mã PIN gồm đúng 6 chữ số.")]
    public string Pin { get; set; } = string.Empty;

    [Required(ErrorMessage = "Vui lòng nhập lại mã PIN.")]
    public string ConfirmPin { get; set; } = string.Empty;
}

/// <summary>Body của POST api/v1/vaults/{vaultId}/pin/change: đổi PIN khi còn nhớ PIN cũ.</summary>
public class ChangeVaultPinRequest
{
    [Required(ErrorMessage = "Vui lòng nhập mã PIN hiện tại.")]
    public string CurrentPin { get; set; } = string.Empty;

    [Required(ErrorMessage = "Vui lòng nhập mã PIN mới.")]
    [RegularExpression(@"^\d{6}$", ErrorMessage = "Mã PIN gồm đúng 6 chữ số.")]
    public string NewPin { get; set; } = string.Empty;

    [Required(ErrorMessage = "Vui lòng nhập lại mã PIN mới.")]
    public string ConfirmPin { get; set; } = string.Empty;
}

/// <summary>Body của POST api/v1/vaults/{vaultId}/pin/reset: quên PIN, xác nhận bằng mật khẩu tài khoản.</summary>
public class ResetVaultPinRequest
{
    [Required(ErrorMessage = "Cần nhập mật khẩu tài khoản để đặt lại mã PIN.")]
    [MaxLength(128)]
    public string Password { get; set; } = string.Empty;

    [Required(ErrorMessage = "Vui lòng nhập mã PIN mới.")]
    [RegularExpression(@"^\d{6}$", ErrorMessage = "Mã PIN gồm đúng 6 chữ số.")]
    public string NewPin { get; set; } = string.Empty;

    [Required(ErrorMessage = "Vui lòng nhập lại mã PIN mới.")]
    public string ConfirmPin { get; set; } = string.Empty;
}
