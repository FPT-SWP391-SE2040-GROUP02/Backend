namespace LegacyVault.Infrastructure.Jobs;

/// <summary>Cấu hình mục "DeathCase" trong appsettings: job gửi thông báo hồ sơ chứng tử cho Owner (D3).</summary>
public sealed class DeathCaseJobSettings
{
    public const string SectionName = "DeathCase";

    /// <summary>false thì job không chạy (test tắt để tự gọi IDeathCaseNoticeJob).</summary>
    public bool NoticeJobEnabled { get; set; } = true;

    /// <summary>Số giây giữa hai lần quét. Mặc định 60.</summary>
    public int NoticePollSeconds { get; set; } = 60;

    /// <summary>D6: false thì job phân công Verifier không chạy (test tắt để tự gọi IDeathCaseAssignmentJob).</summary>
    public bool AssignmentJobEnabled { get; set; } = true;

    /// <summary>D6, NFR-03: số giây giữa hai lần quét phân công. Mặc định 60.</summary>
    public int AssignmentPollSeconds { get; set; } = 60;
}
