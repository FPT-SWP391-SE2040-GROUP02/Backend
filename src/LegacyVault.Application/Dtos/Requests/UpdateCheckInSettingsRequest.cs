using System.ComponentModel.DataAnnotations;

namespace LegacyVault.Application.Dtos.Requests;

/// <summary>
/// Body của PATCH /api/v1/vaults/me/check-in-settings: chọn chu kỳ điểm danh (DMS-01).
/// </summary>
/// <remarks>
/// Giá trị hợp lệ: chu kỳ 30/60/90 ngày. Lựa chọn mới chỉ áp dụng từ kỳ điểm danh sau nếu đang có kỳ chạy.
/// Số thư nhắc trễ và khoảng cách giữa các thư do Admin đặt, chủ kho không chọn.
/// </remarks>
public class UpdateCheckInSettingsRequest
{
    /// <summary>Etag lấy từ CheckInSettingsResponse lúc đọc; chống ghi đè thay đổi của tab khác.</summary>
    [Required(ErrorMessage = "Thiếu etag của dữ liệu đang sửa.")]
    [MaxLength(64)]
    public string Etag { get; set; } = string.Empty;

    public int? CycleDays { get; set; }
}
