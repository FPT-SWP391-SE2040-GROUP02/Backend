using System.ComponentModel.DataAnnotations;

namespace LegacyVault.Application.Dtos.Requests;

public class ChangeUserRoleRequest
{
    /// <summary>USER | VERIFIER | ADMIN.</summary>
    [Required]
    [MaxLength(16)]
    public string Role { get; set; } = string.Empty;

    [Required(ErrorMessage = "Vui lòng ghi lý do.")]
    [MaxLength(500)]
    public string Reason { get; set; } = string.Empty;
}

public class ChangeUserStatusRequest
{
    /// <summary>ACTIVE | LOCKED | DISABLED.</summary>
    [Required]
    [MaxLength(16)]
    public string Status { get; set; } = string.Empty;

    [Required(ErrorMessage = "Vui lòng ghi lý do.")]
    [MaxLength(500)]
    public string Reason { get; set; } = string.Empty;
}

public class ReassignVerifierRequest
{
    [Required]
    public Guid? VerifierUserId { get; set; }

    [Required(ErrorMessage = "Vui lòng ghi lý do.")]
    [MaxLength(500)]
    public string Reason { get; set; } = string.Empty;
}

public class AssignExecutorRequest
{
    /// <summary>Tài khoản được giao làm Người thực thi của kho.</summary>
    [Required]
    public Guid? UserId { get; set; }

    [Required(ErrorMessage = "Vui lòng ghi lý do.")]
    [MaxLength(500)]
    public string Reason { get; set; } = string.Empty;
}

public class CreateAdminUserRequest
{
    [Required(ErrorMessage = "Vui lòng nhập địa chỉ email.")]
    [EmailAddress(ErrorMessage = "Địa chỉ email không đúng định dạng.")]
    [MaxLength(256)]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "Vui lòng nhập họ và tên.")]
    [MaxLength(200)]
    public string FullName { get; set; } = string.Empty;

    /// <summary>Bắt buộc với ADMIN. Bỏ trống với VERIFIER/EXECUTOR thì hệ thống gửi thư mời đặt mật khẩu tới email.</summary>
    [MaxLength(128)]
    public string Password { get; set; } = string.Empty;

    /// <summary>VERIFIER | EXECUTOR | ADMIN. EXECUTOR là Người thực thi hệ thống để Chủ tài sản chọn trong danh bạ.</summary>
    [Required(ErrorMessage = "Vui lòng chọn vai trò.")]
    [MaxLength(16)]
    public string Role { get; set; } = string.Empty;

    [MaxLength(32)]
    public string? Phone { get; set; }

    /// <summary>Số chứng chỉ hành nghề luật sư (VERIFIER hoặc EXECUTOR).</summary>
    [MaxLength(64)]
    public string? PracticeCertificateNumber { get; set; }

    /// <summary>Số thẻ luật sư (VERIFIER hoặc EXECUTOR).</summary>
    [MaxLength(64)]
    public string? LawyerCardNumber { get; set; }

    /// <summary>Vai trò chuyên môn của Người thực thi hệ thống (chỉ dùng cho EXECUTOR).</summary>
    [MaxLength(200)]
    public string? ExecutorRoleTitle { get; set; }

    /// <summary>Đoàn luật sư / tổ chức hành nghề (chỉ dùng cho EXECUTOR).</summary>
    [MaxLength(200)]
    public string? LegalOrganization { get; set; }
}

/// <summary>
/// Admin sửa thông tin của tài khoản Người xét duyệt hoặc Người thực thi hệ thống: chỉ họ tên và số điện thoại. Thay trọn hai
/// trường: số điện thoại để trống thì xóa số cũ. Số chứng chỉ, số thẻ luật sư chỉ nhập lúc tạo tài khoản.
/// </summary>
public class UpdateAdminUserProfileRequest
{
    [Required(ErrorMessage = "Vui lòng nhập họ và tên.")]
    [MaxLength(200)]
    public string FullName { get; set; } = string.Empty;

    [MaxLength(32)]
    public string? Phone { get; set; }
}
