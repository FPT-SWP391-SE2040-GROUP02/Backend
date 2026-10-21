namespace LegacyVault.Infrastructure.Billing;

/// <summary>
/// Mục cấu hình "Billing". Bank chỉ chứa thông tin công khai của tài khoản nhận tiền.
/// Tài khoản thật và SePay:WebhookApiKey (secret) đặt trong user-secrets hoặc biến môi trường, không ghi vào appsettings của repo.
/// </summary>
public class BillingSettings
{
    public const string SectionName = "Billing";

    public BillingBankSection Bank { get; set; } = new BillingBankSection();

    public BillingSePaySection SePay { get; set; } = new BillingSePaySection();

    /// <summary>
    /// Công tắc endpoint giả lập trả tiền (dev-pay). Mặc định tắt; chỉ có tác dụng khi môi trường là Development
    /// (xem BillingDevSettings). Không đặt true trong appsettings.json hay môi trường thật.
    /// </summary>
    public bool DevPaymentEnabled { get; set; }
}

public class BillingBankSection
{
    /// <summary>Mã ngân hàng theo SePay/VietQR, ví dụ "MBBank".</summary>
    public string BankCode { get; set; } = string.Empty;

    public string AccountNumber { get; set; } = string.Empty;

    public string AccountName { get; set; } = string.Empty;
}

public class BillingSePaySection
{
    /// <summary>Secret: key SePay gửi trong header Authorization: Apikey. Chỉ đặt bằng user-secrets / biến môi trường.</summary>
    public string WebhookApiKey { get; set; } = string.Empty;

    /// <summary>"Test" hoặc "Live" (PAY-01). Mặc định Test.</summary>
    public string Environment { get; set; } = "Test";
}
