using LegacyVault.Application.Services;
using Microsoft.Extensions.Options;

namespace LegacyVault.Infrastructure.Billing;

public sealed class BillingSePaySettings : IBillingSePaySettings
{
    private readonly IOptions<BillingSettings> options;

    public BillingSePaySettings(IOptions<BillingSettings> options)
    {
        this.options = options;
    }

    public string WebhookApiKey
    {
        get { return options.Value.SePay.WebhookApiKey; }
    }

    /// <remarks>
    /// Chỉ nhận Test hoặc Live (không phân biệt hoa thường). Giá trị khác là lỗi cấu hình máy chủ: ném lỗi để SePay gửi lại
    /// sau khi sửa, thay vì lưu giao dịch với nhãn môi trường sai.
    /// </remarks>
    public string Environment
    {
        get
        {
            string configured = options.Value.SePay.Environment;
            if (string.Equals(configured, "Test", StringComparison.OrdinalIgnoreCase))
            {
                return "Test";
            }
            if (string.Equals(configured, "Live", StringComparison.OrdinalIgnoreCase))
            {
                return "Live";
            }
            throw new InvalidOperationException("Billing:SePay:Environment chỉ nhận Test hoặc Live.");
        }
    }
}
