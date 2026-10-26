using System.ComponentModel.DataAnnotations;

namespace LegacyVault.Application.Dtos.Requests;

/// <summary>
/// Body của POST /api/v1/check-ins/smartwatch: Điểm danh sinh trắc học qua đồng hồ thông minh (Samsung Galaxy Watch).
/// </summary>
public sealed class SmartwatchCheckInRequest
{
    /// <summary>Nhịp tim đo được từ cảm biến quang học PPG (bpm). Giá trị hợp lệ: 40 - 180 bpm.</summary>
    [Required(ErrorMessage = "Thiếu chỉ số nhịp tim.")]
    [Range(40, 180, ErrorMessage = "Nhịp tim không hợp lệ hoặc cảm biến chưa đo được tín hiệu sống (yêu cầu từ 40 đến 180 bpm).")]
    public int HeartRate { get; set; }

    /// <summary>Trạng thái đồng hồ có đang tiếp xúc với da tay người dùng hay không.</summary>
    [Required(ErrorMessage = "Thiếu trạng thái tiếp xúc da tay.")]
    public bool IsOnWrist { get; set; } = true;
}
