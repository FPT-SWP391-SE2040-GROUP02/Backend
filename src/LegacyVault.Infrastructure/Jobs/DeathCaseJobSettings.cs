namespace LegacyVault.Infrastructure.Jobs;

/// <summary>Cấu hình mục "DeathCase" trong appsettings: các job nền của hồ sơ chứng tử (giao Verifier, ghi nhận quá hạn bổ sung).</summary>
public sealed class DeathCaseJobSettings
{
    public const string SectionName = "DeathCase";

    /// <summary>D6: false thì job phân công Verifier không chạy (test tắt để tự gọi IDeathCaseAssignmentJob).</summary>
    public bool AssignmentJobEnabled { get; set; } = true;

    /// <summary>D6, NFR-03: số giây giữa hai lần quét phân công. Mặc định 60.</summary>
    public int AssignmentPollSeconds { get; set; } = 60;

    /// <summary>false thì job ghi nhận quá hạn bổ sung không chạy (test tắt để tự gọi IDeathCaseSupplementJob).</summary>
    public bool SupplementJobEnabled { get; set; } = true;

    /// <summary>Số giây giữa hai lần quét quá hạn bổ sung. Mặc định 300.</summary>
    public int SupplementPollSeconds { get; set; } = 300;
}
