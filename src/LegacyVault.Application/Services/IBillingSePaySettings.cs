namespace LegacyVault.Application.Services;

/// <summary>
/// Cấu hình nhận webhook SePay, đọc từ "Billing:SePay" (Application không tham chiếu IOptions).
/// WebhookApiKey là secret: chỉ đặt bằng user-secrets hoặc biến môi trường, không ghi vào appsettings, log hay tài liệu.
/// </summary>
public interface IBillingSePaySettings
{
    /// <summary>Key mà SePay gửi trong header Authorization: Apikey. Rỗng nghĩa là chưa cấu hình (webhook từ chối mọi request).</summary>
    string WebhookApiKey { get; }

    /// <summary>"Test" hoặc "Live" (PAY-01: hai môi trường dùng cấu hình và key riêng). Giá trị khác là lỗi cấu hình.</summary>
    string Environment { get; }
}
