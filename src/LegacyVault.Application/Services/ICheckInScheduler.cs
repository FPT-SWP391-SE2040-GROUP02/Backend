using LegacyVault.Domain.Entities;

namespace LegacyVault.Application.Services;

/// <summary>
/// Mở và dừng lịch điểm danh của một kế hoạch. Dành cho module khác gọi (kích hoạt kế hoạch, chuyển Free...).
/// </summary>
/// <remarks>
/// Hai hàm chỉ đánh dấu thay đổi qua repository, KHÔNG gọi SaveChanges và KHÔNG đổi plan.Status:
/// người gọi tự đổi trạng thái kế hoạch rồi lưu một lần cùng thay đổi của mình (STATE-02).
/// </remarks>
public interface ICheckInScheduler
{
    /// <summary>
    /// Mở kỳ điểm danh đầu tiên (SRS §5.2 bước kích hoạt). Đã có kỳ đang mở thì không làm gì,
    /// nên bấm kích hoạt nhiều lần vẫn chỉ có một kỳ (SETUP-14).
    /// </summary>
    Task StartFirstPeriodAsync(HandoverPlan plan, CancellationToken ct);

    /// <summary>Đóng kỳ đang mở khi kế hoạch về DRAFT (SETUP-16, chuyển Free). Không có kỳ mở thì không làm gì.</summary>
    Task StopAsync(HandoverPlan plan, CancellationToken ct);
}
