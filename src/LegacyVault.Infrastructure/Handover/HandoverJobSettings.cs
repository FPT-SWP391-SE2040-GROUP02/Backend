namespace LegacyVault.Infrastructure.Handover;

/// <summary>Cấu hình mục "Handover:Job": job quét lượt/grant hết hạn.</summary>
public sealed class HandoverJobSettings
{
    public const string SectionName = "Handover:Job";

    public bool Enabled { get; set; } = true;

    /// <summary>Chu kỳ quét (giây).</summary>
    public int PollSeconds { get; set; } = 60;
}
