using System.ComponentModel.DataAnnotations;

namespace LegacyVault.Application.Dtos.Requests;

/// <summary>
/// Body của POST /api/v1/billing/quote và POST /api/v1/billing/orders: chỉ có lựa chọn của Owner.
/// </summary>
/// <remarks>
/// Cố ý KHÔNG có trường số tiền, loại đơn hay trạng thái: backend tự tính giá và tự xác định loại đơn (PAY-11, AC-94).
/// Nâng hạng giữa kỳ thì chỉ cần tier là gói đích; years bị bỏ qua.
/// </remarks>
public class BillingSelectionRequest
{
    /// <summary>Mã gói; bắt buộc khi mua mới và nâng hạng.</summary>
    [MaxLength(16, ErrorMessage = "Hạng dịch vụ không hợp lệ.")]
    public string? Tier { get; set; }

    /// <summary>1, 3 hoặc 5; bắt buộc khi mua mới.</summary>
    public int? Years { get; set; }

    /// <summary>
    /// true = gia hạn kỳ hiện tại (khi kho còn hạn). Kho đã hết hạn hoặc đang ân hạn luôn là gia hạn dù không gửi.
    /// </summary>
    public bool Renew { get; set; }
}
