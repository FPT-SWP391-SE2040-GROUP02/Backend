using LegacyVault.Domain.Enums;

namespace LegacyVault.Application.Services;

/// <summary>
/// Một gói trả phí trong bảng giá hiện hành (gói gốc XS, XS_MAX hoặc gói Admin tạo thêm). Billing đọc gói từ đây thay vì
/// enum cố định, nên Admin thêm, ẩn gói mà không đổi code.
/// </summary>
public sealed class PlanInfo
{
    /// <summary>Mã gói trên API: "XS", "XS_MAX" hoặc mã Admin đặt ("GOLD").</summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>Hạng enum lưu trong quyền dịch vụ: gói gốc giữ hạng riêng, gói thêm là <see cref="ServiceTier.Custom"/>.</summary>
    public ServiceTier Tier { get; set; }

    public string Name { get; set; } = string.Empty;

    public string Subtitle { get; set; } = string.Empty;

    /// <summary>Thứ tự gói tăng dần: gói đứng sau là gói cao hơn khi nâng hạng.</summary>
    public int Order { get; set; }

    /// <summary>false khi Admin tạm ngừng mở bán.</summary>
    public bool OnSale { get; set; } = true;

    /// <summary>true khi Admin đã xóa (ẩn) gói: không bán nữa, người đang dùng vẫn dùng đến hết kỳ.</summary>
    public bool Hidden { get; set; }

    /// <summary>Các dòng mô tả tính năng do Admin viết; rỗng thì Client dùng mô tả mặc định.</summary>
    public IReadOnlyList<string> Features { get; set; } = new List<string>();

    public long StorageQuotaBytes { get; set; }

    public int IncludedSlots { get; set; }

    /// <summary>Giá cả kỳ theo số năm, chỉ gồm các kỳ đang bán và có giá.</summary>
    public IReadOnlyDictionary<int, long> Prices { get; set; } = new Dictionary<int, long>();

    /// <summary>Bán được khi đang mở bán và chưa bị xóa.</summary>
    public bool CanBuy
    {
        get { return OnSale && !Hidden; }
    }
}
