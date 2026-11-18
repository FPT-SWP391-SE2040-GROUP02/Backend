namespace LegacyVault.Infrastructure.Handover;

/// <summary>Cấu hình mục "Retention" trong appsettings: job lưu và xóa dữ liệu sau bàn giao.</summary>
public sealed class RetentionJobSettings
{
    public const string SectionName = "Retention";

    /// <summary>false thì job không chạy (test tắt để tự gọi IRetentionJob).</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Số giây giữa hai lần quét. Mặc định 300.</summary>
    public int PollSeconds { get; set; } = 300;
}
