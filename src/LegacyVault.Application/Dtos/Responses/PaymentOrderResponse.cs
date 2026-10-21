namespace LegacyVault.Application.Dtos.Responses;

/// <summary>
/// Đơn thanh toán trả cho FE (SRS §8.3). Không có OwnerId, VaultId, RowVersion;
/// giá, dung lượng, số suất là giá trị đã chốt lúc tạo đơn (PAY-11).
/// </summary>
public class PaymentOrderResponse
{
    public Guid Id { get; set; }

    /// <summary>"PURCHASE" hoặc "ADDON".</summary>
    public string Kind { get; set; } = string.Empty;

    /// <summary>"PENDING", "PAID", "EXPIRED", "CANCELLED".</summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>"XS" hoặc "XS_MAX".</summary>
    public string Tier { get; set; } = string.Empty;

    /// <summary>Null với ADDON.</summary>
    public int? Years { get; set; }

    public int ExtraSlots { get; set; }
    public long TierAmount { get; set; }
    public long SlotAmount { get; set; }
    public long TotalAmount { get; set; }

    /// <summary>"VND".</summary>
    public string Currency { get; set; } = string.Empty;

    public long StorageQuotaBytes { get; set; }

    /// <summary>Suất người nhận kèm theo hạng, đã chốt lúc tạo đơn (suất mua thêm của đơn nằm ở ExtraSlots).</summary>
    public int IncludedBeneficiarySlots { get; set; }

    public string PaymentCode { get; set; } = string.Empty;

    /// <summary>Nội dung chuyển khoản: đúng bằng PaymentCode.</summary>
    public string TransferContent { get; set; } = string.Empty;

    public PaymentBankResponse Bank { get; set; } = new PaymentBankResponse();

    /// <summary>Ảnh QR chuyển khoản; chỉ có khi đơn còn PENDING.</summary>
    public string? QrImageUrl { get; set; }

    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? PaidAt { get; set; }
    public DateTimeOffset? CancelledAt { get; set; }

    /// <summary>Giờ server lúc trả lời, để FE tính thời gian còn lại mà không tin đồng hồ máy khách.</summary>
    public DateTimeOffset ServerTime { get; set; }

    /// <summary>"Thanh toán không tự kích hoạt bàn giao."</summary>
    public string Notice { get; set; } = string.Empty;
}

/// <summary>Tài khoản nhận tiền (thông tin công khai).</summary>
public class PaymentBankResponse
{
    public string BankCode { get; set; } = string.Empty;
    public string AccountNumber { get; set; } = string.Empty;
    public string AccountName { get; set; } = string.Empty;
}
