namespace LegacyVault.Application.Dtos.Responses;

/// <summary>
/// Thông tin kho trả cho FE: trạng thái kho, kế hoạch và quyền dịch vụ TÁCH RIÊNG (§11.1);
/// dung lượng đã dùng/còn lại và số suất (QUOTA-06).
/// </summary>
public class VaultResponse
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }

    /// <summary>"OPEN" hoặc "CLOSED".</summary>
    public string Status { get; set; } = string.Empty;
    public DateTimeOffset? ClosedAt { get; set; }

    /// <summary>"DRAFT", "ACTIVE", "CHECKIN_OVERDUE", ...</summary>
    public string PlanStatus { get; set; } = string.Empty;
    public int PlanConfigVersion { get; set; }
    public DateTimeOffset? PlanActivatedAt { get; set; }

    /// <summary>"FREE", "XS", "XS_MAX".</summary>
    public string ServiceTier { get; set; } = string.Empty;

    /// <summary>"FREE", "PAID_ACTIVE", "GRACE_READ_ONLY", "EXPIRED_REVIEW".</summary>
    public string ServiceStatus { get; set; } = string.Empty;
    public DateTimeOffset? ServiceExpiresAt { get; set; }

    public long StorageQuotaBytes { get; set; }
    public long StorageUsedBytes { get; set; }

    /// <summary>Còn lại = quota − đã dùng (không âm).</summary>
    public long StorageRemainingBytes { get; set; }

    /// <summary>Tổng suất = suất kèm hạng + suất mua thêm.</summary>
    public int BeneficiarySlots { get; set; }

    /// <summary>Số gói chưa lưu trữ (NFR-14: tối đa 50).</summary>
    public int ActiveContainerCount { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }

    /// <summary>Gửi lại trong body (trường etag) khi sửa tên/mô tả kho. Đổi sau mỗi lần sửa.</summary>
    public string Etag { get; set; } = string.Empty;
}
