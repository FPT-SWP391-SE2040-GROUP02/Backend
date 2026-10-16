using LegacyVault.Domain.Enums;

namespace LegacyVault.Domain.Entities;

/// <summary>
/// Lệnh giữ (hold) chặn việc phát hành hoặc xóa trên một phạm vi (bảng Holds), SRS §6.4.
/// </summary>
/// <remarks>
/// - Mỗi hold có phạm vi, loại, lý do, người phụ trách, ngày xem lại (tối đa 7 ngày một lần), thời điểm gỡ.
/// - Hold Vault chặn: duyệt chứng tử, nhận lượt, đồng ý, tải, kích hoạt kế hoạch và dọn dữ liệu của kho.
/// - Lý do KHÔNG được chứa CCCD, token hay nội dung tài sản (SEC-07).
/// - Đặt hoặc gỡ hold ghi plan.UpdatedAt trong cùng giao dịch, nên thao tác nhận/đồng ý chạy cùng lúc thua RowVersion.
/// </remarks>
public class Hold
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid VaultId { get; set; }

    public HoldScope Scope { get; set; }

    /// <summary>Id của đối tượng trong phạm vi (gói, chỉ định, claim, grant); null khi phạm vi là cả kho.</summary>
    public Guid? TargetId { get; set; }

    public HoldType Type { get; set; }

    public HoldStatus Status { get; set; }

    public string Reason { get; set; } = string.Empty;

    /// <summary>Người phụ trách xem lại hold; null chỉ khi chưa có ai (khi đó có sự cố đi kèm).</summary>
    public Guid? ResponsibleUserId { get; set; }

    /// <summary>Ngày xem lại tiếp theo.</summary>
    public DateTimeOffset ReviewAt { get; set; }

    public Guid? CreatedByUserId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? ReleasedAt { get; set; }

    public Guid? ReleasedByUserId { get; set; }

    public string? ReleaseReason { get; set; }

    /// <summary>Concurrency token: gỡ hold hai lần cùng lúc chỉ một bên thắng.</summary>
    public DateTimeOffset? UpdatedAt { get; set; }
}
