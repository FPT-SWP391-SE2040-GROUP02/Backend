namespace LegacyVault.Application.Dtos.Responses;

/// <summary>
/// Doanh thu cho Admin: tổng tiền các giao dịch đã khớp đơn hoặc đã xử lý, gom theo ngày, tháng hoặc quý giờ Việt Nam.
/// Giao dịch lệch chờ đối chiếu và giao dịch chưa đối chiếu chưa được tính.
/// </summary>
public class AdminRevenueResponse
{
    /// <summary>"day", "month" hoặc "quarter".</summary>
    public string GroupBy { get; set; } = "month";

    /// <summary>"Live", "Test" hoặc "ALL". Mặc định chỉ "Live" (tiền thật).</summary>
    public string Environment { get; set; } = "Live";

    /// <summary>Tổng tiền của mọi kỳ.</summary>
    public long TotalAmount { get; set; }

    /// <summary>Số giao dịch được cộng.</summary>
    public int TransactionCount { get; set; }

    public string Currency { get; set; } = "VND";

    /// <summary>Các kỳ có doanh thu, cũ trước, mới sau. Kỳ không có giao dịch thì không xuất hiện.</summary>
    public List<AdminRevenueBucketResponse> Buckets { get; set; } = new List<AdminRevenueBucketResponse>();
}

/// <summary>Doanh thu của một kỳ.</summary>
public class AdminRevenueBucketResponse
{
    /// <summary>Khóa sắp xếp được: "2026-11-05", "2026-11" hoặc "2026-Q4".</summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>Nhãn hiển thị: "05/11/2026", "11/2026" hoặc "Quý 4/2026".</summary>
    public string Label { get; set; } = string.Empty;

    public long Amount { get; set; }

    public int Count { get; set; }
}
