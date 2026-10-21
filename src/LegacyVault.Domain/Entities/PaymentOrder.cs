using LegacyVault.Domain.Enums;

namespace LegacyVault.Domain.Entities;

/// <summary>
/// Đơn thanh toán của một kho (bảng PaymentOrders), SRS §8.1–8.3, §11.5.
/// </summary>
/// <remarks>
/// - Giá, dung lượng, số suất được chốt lúc tạo đơn theo PolicyVersion (PAY-11); đổi bảng giá sau đó không ảnh hưởng đơn cũ.
/// - Mỗi kho chỉ có một đơn Pending (PAY-04): unique có lọc trên VaultId khi Status = Pending.
/// - PaymentCode là nội dung chuyển khoản, unique toàn hệ thống.
/// - RowVersion là concurrency token: hai lần ghi nhận thanh toán song song chỉ một bên thắng, không cấp quyền hai lần.
/// - Số tiền tính bằng VND (đồng, số nguyên).
/// </remarks>
public class PaymentOrder
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid VaultId { get; set; }

    /// <summary>Owner tạo đơn.</summary>
    public Guid OwnerId { get; set; }

    public PaymentOrderKind Kind { get; set; }

    /// <summary>Hạng được mua; với ADDON là hạng hiện tại của kho.</summary>
    public ServiceTier Tier { get; set; }

    /// <summary>Số năm (1, 3, 5); null với ADDON.</summary>
    public int? Years { get; set; }

    /// <summary>Số suất người nhận mua thêm (q).</summary>
    public int ExtraSlots { get; set; }

    /// <summary>Tiền hạng dịch vụ; 0 với ADDON.</summary>
    public long TierAmount { get; set; }

    /// <summary>Tiền suất mua thêm.</summary>
    public long SlotAmount { get; set; }

    public long TotalAmount { get; set; }

    /// <summary>Dung lượng chốt theo bảng giá (ADDON: dung lượng hiện tại).</summary>
    public long StorageQuotaBytes { get; set; }

    /// <summary>Suất kèm theo hạng, chốt theo bảng giá.</summary>
    public int IncludedBeneficiarySlots { get; set; }

    /// <summary>ADDON: hạn kỳ dịch vụ lúc tạo đơn, để lúc trả phát hiện kỳ đã đổi; PURCHASE: null.</summary>
    public DateTimeOffset? BaseServiceExpiresAt { get; set; }

    /// <summary>Phiên bản bảng giá đã dùng, ví dụ "2026-10".</summary>
    public string PolicyVersion { get; set; } = string.Empty;

    /// <summary>Mã thanh toán, cũng là nội dung chuyển khoản.</summary>
    public string PaymentCode { get; set; } = string.Empty;

    public PaymentOrderStatus Status { get; set; }

    /// <summary>Hạn thanh toán của đơn.</summary>
    public DateTimeOffset ExpiresAt { get; set; }

    public DateTimeOffset? PaidAt { get; set; }

    public DateTimeOffset? CancelledAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public byte[] RowVersion { get; set; } = Array.Empty<byte>();
}
