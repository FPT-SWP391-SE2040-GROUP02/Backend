using LegacyVault.Domain.Enums;

namespace LegacyVault.Domain.Entities;

/// <summary>
/// Kho của một Owner (bảng Vaults), SRS §5.2 bước 1.
/// </summary>
/// <remarks>
/// - SETUP-12 / DATA-04: mỗi OwnerId có tối đa MỘT kho trong hệ thống, kể cả kho đã Closed.
///   Cần unique index trên OwnerId (không lọc theo Status); tạo đồng thời không được sinh kho thứ hai (AC-89).
/// - Kho mới luôn đi kèm đúng một Plan (Draft) và một Entitlement (Free, 20 MiB, 0 suất), tạo cùng một giao dịch.
/// - Tạo kho, thanh toán và kích hoạt kế hoạch là ba kết quả khác nhau.
/// </remarks>
public class Vault
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid OwnerId { get; set; }

    public User Owner { get; set; } = null!;

    public string Name { get; set; } = string.Empty;

    /// <summary>Không mã hóa: không ghi mật khẩu, seed phrase vào đây (ASSET-02).</summary>
    public string? Description { get; set; }

    public VaultStatus Status { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Đổi sau mỗi lần sửa; dùng làm ETag chống ghi đè khi sửa.</summary>
    public DateTimeOffset? UpdatedAt { get; set; }

    /// <summary>Thời điểm chuyển Closed (SETUP-18); null khi còn mở.</summary>
    public DateTimeOffset? ClosedAt { get; set; }

    public HandoverPlan Plan { get; set; } = null!;

    public VaultEntitlement Entitlement { get; set; } = null!;

    public List<Container> Containers { get; set; } = new List<Container>();
}
