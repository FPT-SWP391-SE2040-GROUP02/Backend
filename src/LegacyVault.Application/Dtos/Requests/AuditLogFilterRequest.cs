using System.ComponentModel.DataAnnotations;

namespace LegacyVault.Application.Dtos.Requests;

/// <summary>Bộ lọc nhật ký hoạt động đọc từ query string (ADMIN-06: lọc theo thời gian, loại thao tác).</summary>
public class AuditLogFilterRequest
{
    /// <summary>Loại thao tác dạng UPPER_SNAKE_CASE, ví dụ CONTAINER_CREATED; bỏ trống = mọi loại.</summary>
    [MaxLength(64, ErrorMessage = "Loại thao tác tối đa 64 ký tự.")]
    public string? Action { get; set; }

    /// <summary>Từ thời điểm này (tính cả mốc).</summary>
    public DateTimeOffset? From { get; set; }

    /// <summary>Trước thời điểm này (không tính mốc).</summary>
    public DateTimeOffset? To { get; set; }
}
