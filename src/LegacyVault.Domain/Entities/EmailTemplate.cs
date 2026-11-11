namespace LegacyVault.Domain.Entities;

/// <summary>
/// Chữ do Admin đặt cho một loại thư thông báo. Mỗi loại thư (Key) có tối đa một dòng; không có dòng thì thư dùng chữ gốc trong mã.
/// Trường để trống (null) nghĩa là giữ chữ gốc của trường đó.
/// </summary>
public class EmailTemplate
{
    /// <summary>Mã loại thư, ví dụ PLAN_ACTIVATED.</summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>false: hệ thống không gửi loại thư này nữa (chỉ áp dụng cho loại thư cho phép tắt).</summary>
    public bool Enabled { get; set; } = true;

    public string? Subject { get; set; }

    public string? Title { get; set; }

    /// <summary>Nội dung thân thư; mỗi dòng là một đoạn.</summary>
    public string? Body { get; set; }

    public string? ButtonText { get; set; }

    public string? Note { get; set; }

    public Guid? UpdatedByUserId { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}
