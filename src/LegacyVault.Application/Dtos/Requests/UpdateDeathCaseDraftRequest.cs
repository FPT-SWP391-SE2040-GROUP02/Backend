using System.ComponentModel.DataAnnotations;

namespace LegacyVault.Application.Dtos.Requests;

/// <summary>
/// Body của PUT /api/v1/executor/death-cases/{caseId}/draft: sửa bản nháp hiện tại của hồ sơ chứng tử (DEATH-01).
/// </summary>
/// <remarks>
/// Trường chuỗi (kể cả mã tỉnh, mã phường): null/không gửi = giữ nguyên, "" = xóa. Ngày: null/không gửi = giữ nguyên.
/// OwnerCitizenId được BE chuẩn hóa (bỏ khoảng trắng, đúng 12 chữ số) và mã hóa; response chỉ trả 4 số cuối (DATA-02).
/// </remarks>
public class UpdateDeathCaseDraftRequest
{
    /// <summary>Giá trị Etag lấy từ DeathCaseResponse lúc FE đọc hồ sơ.</summary>
    [Required(ErrorMessage = "Thiếu etag của dữ liệu đang sửa.")]
    [MaxLength(64)]
    public string Etag { get; set; } = string.Empty;

    [MaxLength(200, ErrorMessage = "Họ tên Owner tối đa 200 ký tự.")]
    public string? OwnerFullName { get; set; }

    [MaxLength(32, ErrorMessage = "Số CCCD của Owner quá dài.")]
    public string? OwnerCitizenId { get; set; }

    public DateOnly? DateOfDeath { get; set; }

    [MaxLength(100, ErrorMessage = "Số giấy chứng tử tối đa 100 ký tự.")]
    public string? CertificateNumber { get; set; }

    /// <summary>Ngày đăng ký khai tử ghi trên giấy chứng tử (cột IssuedOn).</summary>
    public DateOnly? IssuedOn { get; set; }

    [MaxLength(200, ErrorMessage = "Nơi cấp tối đa 200 ký tự.")]
    public string? IssuedBy { get; set; }

    /// <summary>Mã tỉnh/thành phố nơi đăng ký khai tử (chọn từ GET /administrative-units/provinces). Đổi tỉnh mà không gửi phường thì xóa phường.</summary>
    [MaxLength(10, ErrorMessage = "Mã tỉnh/thành phố quá dài.")]
    public string? DeathRegistrationProvinceCode { get; set; }

    /// <summary>Mã phường/xã nơi đăng ký khai tử; phải thuộc đúng tỉnh đã chọn.</summary>
    [MaxLength(10, ErrorMessage = "Mã phường/xã quá dài.")]
    public string? DeathRegistrationWardCode { get; set; }
}
