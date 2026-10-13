using LegacyVault.Domain.Enums;

namespace LegacyVault.Domain.Entities;

/// <summary>
/// Quyền dịch vụ hiện hành của một kho (bảng VaultEntitlements), quan hệ 1–1 với Vault.
/// </summary>
/// <remarks>
/// §11.6: quota/suất trên kho CHỈ được cập nhật từ nghiệp vụ thanh toán (luồng P), không có API cho Admin sửa.
/// Kho mới: Tier = Free, Status = Free, 20 MiB (QUOTA-01: 1 MiB = 1.048.576 byte), 0 suất.
/// Kỳ dịch vụ/đơn hàng chi tiết sẽ là bảng riêng khi làm thanh toán.
/// </remarks>
public class VaultEntitlement
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid VaultId { get; set; }

    public Vault Vault { get; set; } = null!;

    public ServiceTier Tier { get; set; }

    public EntitlementStatus Status { get; set; }

    public long StorageQuotaBytes { get; set; }

    /// <summary>Suất người nhận kèm theo hạng.</summary>
    public int IncludedBeneficiarySlots { get; set; }

    /// <summary>Suất mua thêm (add-on).</summary>
    public int ExtraBeneficiarySlots { get; set; }

    /// <summary>Hết kỳ dịch vụ trả phí; null với Free.</summary>
    public DateTimeOffset? ServiceExpiresAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? UpdatedAt { get; set; }
}
