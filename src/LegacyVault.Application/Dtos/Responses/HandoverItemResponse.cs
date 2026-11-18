namespace LegacyVault.Application.Dtos.Responses;

/// <summary>Một tài sản trong lượt bàn giao mà người nhận thấy: tên, loại, tên tệp, loại tệp và kích thước. Không có nội dung.</summary>
public class HandoverItemResponse
{
    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;

    /// <summary>FILE hoặc TEXT.</summary>
    public string Kind { get; set; } = string.Empty;

    public string? FileName { get; set; }

    public string ContentType { get; set; } = string.Empty;

    public long SizeBytes { get; set; }
}
