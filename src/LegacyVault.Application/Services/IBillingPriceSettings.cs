using LegacyVault.Domain.Enums;

namespace LegacyVault.Application.Services;

/// <summary>
/// Bảng giá đọc từ cấu hình "Billing:Pricing" (Application không tham chiếu IOptions).
/// Chỉ gồm các con số tiền và phiên bản chính sách; hạn mức, số năm bán và các mốc thời gian vẫn là luật trong <see cref="BillingPrices"/>.
/// </summary>
/// <remarks>
/// Đổi giá = sửa cấu hình, đổi PolicyVersion rồi khởi động lại API. Đơn đã tạo giữ giá và PolicyVersion đã chốt (PAY-11).
/// </remarks>
public interface IBillingPriceSettings
{
    /// <summary>Phiên bản chính sách giá, ghi vào đơn lúc tạo (PAY-11). Mỗi lần đổi giá phải đổi giá trị này.</summary>
    string PolicyVersion { get; }

    /// <summary>Giá một suất người nhận thêm cho một năm (ADDON-01).</summary>
    long SlotPricePerYear { get; }

    /// <summary>Giá hạng cho cả kỳ (không nhân thêm theo năm). Hạng hoặc số năm không bán → ArgumentOutOfRangeException.</summary>
    long GetTierPrice(ServiceTier tier, int years);
}
