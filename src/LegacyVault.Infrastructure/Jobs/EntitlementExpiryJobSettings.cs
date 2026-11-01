namespace LegacyVault.Infrastructure.Jobs;

/// <summary>Cấu hình mục "EntitlementExpiry" trong appsettings: job nền chuyển kho hết hạn sang ân hạn.</summary>
public sealed class EntitlementExpiryJobSettings
{
    public const string SectionName = "EntitlementExpiry";

    /// <summary>false thì job không chạy (dùng khi test hoặc tắt tạm).</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Số giây giữa hai lần quét. Mặc định 300 (5 phút): hạn dịch vụ tính theo ngày nên không cần quét dày.</summary>
    public int PollSeconds { get; set; } = 300;
}
