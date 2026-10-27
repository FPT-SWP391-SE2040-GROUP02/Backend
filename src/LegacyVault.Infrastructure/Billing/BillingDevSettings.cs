using LegacyVault.Application.Services;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace LegacyVault.Infrastructure.Billing;

/// <summary>
/// D9: dev-pay chỉ hoạt động ở Development và khi cấu hình bật. Môi trường khác luôn tắt, dù cấu hình ghi true.
/// </summary>
public sealed class BillingDevSettings : IBillingDevSettings
{
    private readonly IOptions<BillingSettings> options;
    private readonly IHostEnvironment environment;

    public BillingDevSettings(IOptions<BillingSettings> options, IHostEnvironment environment)
    {
        this.options = options;
        this.environment = environment;
    }

    public bool DevPaymentEnabled
    {
        get
        {
            return options.Value.DevPaymentEnabled;
        }
    }
}
