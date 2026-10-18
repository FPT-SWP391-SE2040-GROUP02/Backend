namespace LegacyVault.Infrastructure.Billing;

/// <summary>
/// Mục cấu hình "Billing". Chỉ chứa thông tin công khai của tài khoản nhận tiền; không có secret ở đây.
/// Tài khoản thật đặt trong user-secrets hoặc biến môi trường, không ghi vào appsettings của repo.
/// </summary>
public class BillingSettings
{
    public const string SectionName = "Billing";

    public BillingBankSection Bank { get; set; } = new BillingBankSection();
}

public class BillingBankSection
{
    /// <summary>Mã ngân hàng theo SePay/VietQR, ví dụ "MBBank".</summary>
    public string BankCode { get; set; } = string.Empty;

    public string AccountNumber { get; set; } = string.Empty;

    public string AccountName { get; set; } = string.Empty;
}
