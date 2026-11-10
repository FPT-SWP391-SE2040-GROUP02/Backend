using LegacyVault.Application.Dtos.Requests;
using LegacyVault.Application.Dtos.Responses;

namespace LegacyVault.Application.Services;

/// <summary>
/// Đồng hồ DEMO: giờ thật cộng một độ lệch. Chỉ có tác dụng ở Development/Staging; môi trường khác dùng bản không làm gì
/// (độ lệch luôn 0, không cho đặt).
/// </summary>
public interface IDemoClock
{
    /// <summary>false ở môi trường không cho tua (Production).</summary>
    bool IsEnabled { get; }

    /// <summary>Giờ thật của máy chủ, không cộng độ lệch.</summary>
    DateTimeOffset RealNow { get; }

    /// <summary>Độ lệch hiện tại giữa giờ hệ thống và giờ thật.</summary>
    TimeSpan Offset { get; }

    /// <summary>Đặt độ lệch mới. Chỉ gọi khi IsEnabled là true.</summary>
    void SetOffset(TimeSpan offset);
}

/// <summary>Chạy mọi việc nền của hệ thống đúng một lượt theo giờ hệ thống hiện tại (giống các monitor nền).</summary>
public interface IDemoJobRunner
{
    Task<DemoJobPassResult> RunAllJobsOnceAsync(CancellationToken ct);
}

/// <summary>Kết quả một lượt chạy việc nền.</summary>
public sealed class DemoJobPassResult
{
    public int ItemsProcessed { get; set; }
    public int EmailsSent { get; set; }
    public int Failures { get; set; }
}

/// <summary>
/// Tua giờ hệ thống tới một ngày để demo: dừng ở từng mốc (nhắc điểm danh, đến hạn, quá hạn, hết hạn lời mời...) và
/// chạy việc nền ở mỗi mốc để mọi email nhắc được gửi như thật. Chỉ chạy ở Development/Staging.
/// </summary>
public interface IDemoClockService
{
    /// <summary>Giờ hệ thống và giờ thật. Không cần đăng nhập.</summary>
    Task<DemoClockStatusResponse> GetStatusAsync(CancellationToken ct);

    /// <summary>
    /// Tua tới ngày chọn (9:00 sáng giờ Việt Nam). Lỗi: 400 VALIDATION_ERROR (ngày sai định dạng, không sau giờ hệ thống,
    /// vượt 400 ngày), 404 USER_NOT_FOUND (không phải Admin), 409 DEMO_CLOCK_BUSY (đang có lần tua khác).
    /// </summary>
    Task<DemoClockTravelResponse> TravelToAsync(DemoClockTravelRequest request, CancellationToken ct);

    /// <summary>
    /// Đưa giờ hệ thống về giờ thật. Dữ liệu sinh ra trong lúc tua vẫn giữ; muốn làm lại từ đầu phải đặt lại dữ liệu trong DB.
    /// </summary>
    Task<DemoClockStatusResponse> ResetAsync(CancellationToken ct);
}
