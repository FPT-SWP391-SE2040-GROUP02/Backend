using LegacyVault.Domain.Enums;

namespace LegacyVault.Domain.Entities;

/// <summary>
/// Một tệp giấy tờ của hồ sơ báo tin (bảng HandoverReportFiles). Nội dung mã hóa; chỉ người nộp và luật sư được giao tải được.
/// </summary>
public class HandoverReportFile
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid ReportId { get; set; }

    public HandoverReport Report { get; set; } = null!;

    public HandoverReportFileKind Kind { get; set; }

    public string FileName { get; set; } = string.Empty;

    public string ContentType { get; set; } = string.Empty;

    public long SizeBytes { get; set; }

    public string StorageKey { get; set; } = string.Empty;

    public string EncryptionKeyId { get; set; } = string.Empty;

    public byte[] WrappedDataKey { get; set; } = Array.Empty<byte>();

    public DateTimeOffset CreatedAt { get; set; }
}
