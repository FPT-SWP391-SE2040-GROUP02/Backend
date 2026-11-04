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

    /// <summary>Số suất thêm tối đa trong một đơn theo cấu hình hiện hành (Admin chỉnh được).</summary>
    int MaxExtraSlotsPerOrder { get; }

    /// <summary>Thời gian đơn chờ thanh toán kể từ lúc tạo theo cấu hình hiện hành (Admin chỉnh được).</summary>
    TimeSpan OrderLifetime { get; }

    /// <summary>Các dòng mô tả tính năng của gói Free do Admin viết; rỗng thì Client dùng mô tả mặc định.</summary>
    IReadOnlyList<string> FreeFeatures { get; }

    /// <summary>Mọi gói trả phí (kể cả tạm ngừng và đã xóa), theo thứ tự tăng dần.</summary>
    IReadOnlyList<PlanInfo> Plans { get; }

    /// <summary>Tìm gói theo mã (không phân biệt hoa thường); không có → null.</summary>
    PlanInfo? FindPlan(string code);

    /// <summary>Các kỳ (số năm) đang bán, tăng dần. Admin thêm, bớt hoặc tạm ngừng từng kỳ.</summary>
    IReadOnlyList<int> SaleYears { get; }

    /// <summary>true khi kỳ này đang bán và hạng này có giá cho kỳ đó.</summary>
    bool HasTierPrice(ServiceTier tier, int years);

    /// <summary>Dung lượng của hạng theo cấu hình hiện hành (Admin chỉnh được). Hạng không bán → ArgumentOutOfRangeException.</summary>
    long GetStorageQuotaBytes(ServiceTier tier);

    /// <summary>Số suất người nhận kèm theo hạng theo cấu hình hiện hành (Admin chỉnh được).</summary>
    int GetIncludedSlots(ServiceTier tier);

    /// <summary>Giá hạng cho cả kỳ (không nhân thêm theo năm). Hạng hoặc số năm không bán → ArgumentOutOfRangeException.</summary>
    long GetTierPrice(ServiceTier tier, int years);
}
