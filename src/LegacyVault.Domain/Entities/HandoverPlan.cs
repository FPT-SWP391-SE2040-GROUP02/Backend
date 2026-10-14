using LegacyVault.Domain.Enums;

namespace LegacyVault.Domain.Entities;

/// <summary>
/// Kế hoạch bàn giao của một kho (bảng HandoverPlans), quan hệ 1–1 với Vault.
/// </summary>
/// <remarks>
/// SETUP-13: mỗi Vault có một kế hoạch hiện hành và một lịch điểm danh; Container không có đồng hồ riêng.
/// Kế hoạch mới luôn Draft; chỉ Owner chủ động kích hoạt mới chuyển Active (SETUP-01).
/// Lịch điểm danh, Executor, chính sách đã chấp thuận sẽ thêm khi làm phần kích hoạt kế hoạch.
/// </remarks>
public class HandoverPlan
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid VaultId { get; set; }

    public Vault Vault { get; set; } = null!;

    public PlanStatus Status { get; set; }

    /// <summary>
    /// SETUP-04: tăng mỗi khi cấu hình kế hoạch đổi (tạo/đổi tên/lưu trữ Container, thêm/gỡ tài sản/chỉ định...).
    /// Snapshot khi nộp chứng tử ghi lại số này. Bắt đầu từ 1.
    /// </summary>
    public int ConfigVersion { get; set; }

    /// <summary>Lần kích hoạt gần nhất; null khi chưa từng kích hoạt.</summary>
    public DateTimeOffset? ActivatedAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? UpdatedAt { get; set; }

    /// <summary>
    /// Số phiên bản của dòng, database tự tăng mỗi lần lưu. Kích hoạt kế hoạch, nộp chứng tử và việc điểm danh
    /// cùng sửa Status; nhờ số này, thao tác lưu sau không ghi đè lên thao tác vừa lưu trước.
    /// </summary>
    public byte[] RowVersion { get; set; } = Array.Empty<byte>();
}
