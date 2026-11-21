using LegacyVault.Domain.Enums;

namespace LegacyVault.Domain.Entities;

/// <summary>
/// Tệp đính kèm biên bản bàn giao (bảng HandoverRecordFiles): giấy tờ người nhận nộp hoặc tệp căn cứ luật sư đính kèm.
/// </summary>
/// <remarks>
/// Nội dung mã hóa envelope trong kho lưu trữ như tài sản; không có link công khai. Chỉ luật sư của biên bản và người
/// đã nộp tệp tải được. Không log tên tệp hay khóa lưu trữ.
/// </remarks>
public class HandoverRecordFile
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid RecordId { get; set; }

    public HandoverRecord Record { get; set; } = null!;

    /// <summary>Yêu cầu nhận kèm tệp này; null với tệp căn cứ của luật sư.</summary>
    public Guid? ClaimId { get; set; }

    public HandoverRecordFileKind Kind { get; set; }

    public string FileName { get; set; } = string.Empty;

    public string ContentType { get; set; } = string.Empty;

    public long SizeBytes { get; set; }

    public string StorageKey { get; set; } = string.Empty;

    public string EncryptionKeyId { get; set; } = string.Empty;

    public byte[] WrappedDataKey { get; set; } = Array.Empty<byte>();

    public Guid UploadedByUserId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
