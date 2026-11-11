namespace LegacyVault.Domain.Entities;

/// <summary>
/// Một phiên bản của bộ mẫu biên bản đồng ý và văn bản từ chối do Admin lưu. Bản đã lưu không sửa nữa: mỗi lần Admin lưu là một dòng mới
/// (Id tăng dần là số phiên bản), để biên bản đã tạo giữ đúng chữ của phiên bản lúc tạo. Chưa có dòng nào thì dùng mẫu gốc trong mã.
/// </summary>
public class HandoverTemplateSet
{
    /// <summary>Số phiên bản, tăng dần.</summary>
    public int Id { get; set; }

    /// <summary>Danh sách đoạn của biên bản đồng ý dạng JSON.</summary>
    public string AcceptJson { get; set; } = string.Empty;

    /// <summary>Danh sách đoạn của văn bản từ chối dạng JSON.</summary>
    public string DeclineJson { get; set; } = string.Empty;

    /// <summary>Câu báo khi có người nhận từ chối.</summary>
    public string DeclineNotice { get; set; } = string.Empty;

    /// <summary>Ghi chú của Admin về lần sửa này.</summary>
    public string? Note { get; set; }

    public Guid? CreatedByUserId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
