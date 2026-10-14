namespace LegacyVault.Infrastructure.Jobs;

/// <summary>Cấu hình mục "CheckIn" trong appsettings: job nền điểm danh.</summary>
public sealed class CheckInJobSettings
{
    public const string SectionName = "CheckIn";

    /// <summary>false thì job không chạy (dùng khi test hoặc tắt tạm).</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Số giây giữa hai lần quét. Mặc định 60.</summary>
    public int PollSeconds { get; set; } = 60;
}
