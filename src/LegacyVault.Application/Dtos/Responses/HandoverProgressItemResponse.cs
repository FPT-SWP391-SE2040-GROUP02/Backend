namespace LegacyVault.Application.Dtos.Responses;

/// <summary>Một tài sản trong lượt bàn giao: chỉ tên, loại và việc đã tải hay chưa, không có nội dung.</summary>
public class HandoverProgressItemResponse
{
    public Guid ItemId { get; set; }

    public string Name { get; set; } = string.Empty;

    /// <summary>FILE hoặc TEXT.</summary>
    public string Kind { get; set; } = string.Empty;

    /// <summary>Đã có ít nhất một lần tải thành công. Lần tải bị từ chối hoặc lỗi toàn vẹn không tính.</summary>
    public bool Downloaded { get; set; }

    public DateTimeOffset? FirstDownloadedAt { get; set; }
}
