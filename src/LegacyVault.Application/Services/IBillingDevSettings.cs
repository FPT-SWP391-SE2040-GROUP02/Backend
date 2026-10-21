namespace LegacyVault.Application.Services;

/// <summary>
/// Công tắc giả lập trả tiền (dev-pay, D9). Chỉ bật khi môi trường là Development VÀ cấu hình
/// Billing:DevPaymentEnabled = true. Application không tham chiếu IOptions hay môi trường host.
/// </summary>
public interface IBillingDevSettings
{
    bool DevPaymentEnabled { get; }
}
