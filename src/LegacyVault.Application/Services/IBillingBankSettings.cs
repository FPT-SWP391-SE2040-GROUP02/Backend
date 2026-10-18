namespace LegacyVault.Application.Services;

/// <summary>
/// Tài khoản ngân hàng nhận tiền, đọc từ cấu hình "Billing:Bank" (Application không tham chiếu IOptions).
/// Chỉ là thông tin công khai để hiện QR cho Owner; không chứa secret.
/// </summary>
public interface IBillingBankSettings
{
    string BankCode { get; }

    string AccountNumber { get; }

    string AccountName { get; }
}
