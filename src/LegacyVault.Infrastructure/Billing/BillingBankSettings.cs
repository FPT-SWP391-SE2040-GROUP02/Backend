using LegacyVault.Application.ServiceConfig;
using LegacyVault.Application.Services;

namespace LegacyVault.Infrastructure.Billing;

/// <summary>Tài khoản nhận tiền đang có hiệu lực: dòng Admin đã lưu trên web đè "Billing:Bank" trong appsettings.</summary>
public sealed class BillingBankSettings : IBillingBankSettings
{
    private readonly ISyncConfigReader reader;

    public BillingBankSettings(ISyncConfigReader reader)
    {
        this.reader = reader;
    }

    public string BankCode
    {
        get { return Read("bankCode"); }
    }

    public string AccountNumber
    {
        get { return Read("accountNumber"); }
    }

    public string AccountName
    {
        get { return Read("accountName"); }
    }

    private string Read(string name)
    {
        string? value = null;
        ResolvedConfig? config = reader.Read(SystemConfigServices.SePay);
        if (config != null && config.Settings.TryGetValue(name, out value))
        {
            return value;
        }
        return string.Empty;
    }
}
