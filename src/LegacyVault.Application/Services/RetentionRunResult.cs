namespace LegacyVault.Application.Services;

/// <summary>Kết quả một lần xử lý kế hoạch của job lưu dữ liệu (để log/test).</summary>
public sealed class RetentionRunResult
{
    public int TurnsDeleted { get; set; }

    public int AssetsShredded { get; set; }

    public int NoticesSent { get; set; }

    public bool VaultClosed { get; set; }
}
