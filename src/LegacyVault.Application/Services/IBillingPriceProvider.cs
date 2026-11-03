namespace LegacyVault.Application.Services;

/// <summary>
/// Cấp bảng giá đang áp dụng cho mỗi lần báo giá hoặc tạo đơn.
/// Giá do Admin chỉnh trong "Bảng so sánh gói dịch vụ" (bảng SystemSettings) đè lên cấu hình "Billing:Pricing".
/// </summary>
/// <remarks>
/// Mỗi lần gọi trả về một bảng giá đã chốt (không đổi giữa chừng một yêu cầu). Đơn đã tạo giữ giá và PolicyVersion đã chốt (PAY-11).
/// </remarks>
public interface IBillingPriceProvider
{
    Task<IBillingPriceSettings> GetCurrentAsync(CancellationToken ct);
}
