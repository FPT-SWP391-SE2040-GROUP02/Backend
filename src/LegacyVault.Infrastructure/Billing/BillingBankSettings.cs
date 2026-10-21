using LegacyVault.Application.Services;
using Microsoft.Extensions.Options;

namespace LegacyVault.Infrastructure.Billing;

public sealed class BillingBankSettings : IBillingBankSettings
{
    private readonly IOptions<BillingSettings> options;

    public BillingBankSettings(IOptions<BillingSettings> options)
    {
        this.options = options;
    }

    public string BankCode
    {
        get { return options.Value.Bank.BankCode; }
    }

    public string AccountNumber
    {
        get { return options.Value.Bank.AccountNumber; }
    }

    public string AccountName
    {
        get { return options.Value.Bank.AccountName; }
    }
}
