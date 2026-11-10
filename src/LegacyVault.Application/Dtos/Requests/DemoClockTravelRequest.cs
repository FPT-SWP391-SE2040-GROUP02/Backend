using System.ComponentModel.DataAnnotations;

namespace LegacyVault.Application.Dtos.Requests;

/// <summary>Admin chọn ngày để tua đồng hồ hệ thống tới (công cụ DEMO, chỉ có ở Development/Staging).</summary>
public class DemoClockTravelRequest
{
    /// <summary>Ngày cần tua tới, dạng yyyy-MM-dd. Hệ thống đặt giờ là 9:00 sáng giờ Việt Nam của ngày đó.</summary>
    [Required(ErrorMessage = "Hãy chọn ngày cần tua tới.")]
    [StringLength(10, MinimumLength = 10, ErrorMessage = "Ngày phải có dạng yyyy-MM-dd.")]
    public string TargetDate { get; set; } = string.Empty;
}
