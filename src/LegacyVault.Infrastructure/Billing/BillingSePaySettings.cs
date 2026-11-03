using LegacyVault.Application.ServiceConfig;
using LegacyVault.Application.Services;

namespace LegacyVault.Infrastructure.Billing;

/// <summary>Khóa webhook và môi trường SePay đang có hiệu lực: dòng Admin đã lưu trên web đè "Billing:SePay" trong appsettings.</summary>
public sealed class BillingSePaySettings : IBillingSePaySettings
{
    private readonly ISyncConfigReader reader;

    public BillingSePaySettings(ISyncConfigReader reader)
    {
        this.reader = reader;
    }

    public string WebhookApiKey
    {
        get
        {
            string? key = null;
            ResolvedConfig? config = reader.Read(SystemConfigServices.SePay);
            if (config != null && config.Secrets.TryGetValue("webhookApiKey", out key))
            {
                return key;
            }
            return string.Empty;
        }
    }

    /// <remarks>
    /// Chỉ nhận Test hoặc Live (không phân biệt hoa thường). Giá trị khác là lỗi cấu hình máy chủ: ném lỗi để SePay gửi lại
    /// sau khi sửa, thay vì lưu giao dịch với nhãn môi trường sai.
    /// </remarks>
    public string Environment
    {
        get
        {
            string? configured = null;
            ResolvedConfig? config = reader.Read(SystemConfigServices.SePay);
            if (config == null || !config.Settings.TryGetValue("environment", out configured))
            {
                configured = "Test";
            }
            if (string.Equals(configured, "Test", StringComparison.OrdinalIgnoreCase))
            {
                return "Test";
            }
            if (string.Equals(configured, "Live", StringComparison.OrdinalIgnoreCase))
            {
                return "Live";
            }
            throw new InvalidOperationException("Môi trường SePay chỉ nhận Test hoặc Live.");
        }
    }
}
