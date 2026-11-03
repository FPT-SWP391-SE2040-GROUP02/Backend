using LegacyVault.Application.Services;
using LegacyVault.Domain.Entities;
using LegacyVault.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LegacyVault.Infrastructure.Billing;

/// <summary>
/// Cấp bảng giá cho billing: lấy cấu hình "Billing:Pricing" (appsettings, user-secrets) rồi đè bằng giá Admin đã chỉnh
/// trong bảng SystemSettings. Đọc DB mỗi lần để giá mới có hiệu lực ngay, không cần khởi động lại API.
/// </summary>
public sealed class BillingPriceProvider : IBillingPriceProvider
{
    private readonly AppDbContext db;
    private readonly IOptions<BillingSettings> options;
    private readonly IBillingPriceSettings configuredPrices;
    private readonly ILogger<BillingPriceProvider> logger;

    public BillingPriceProvider(
        AppDbContext db,
        IOptions<BillingSettings> options,
        IBillingPriceSettings configuredPrices,
        ILogger<BillingPriceProvider> logger)
    {
        this.db = db;
        this.options = options;
        this.configuredPrices = configuredPrices;
        this.logger = logger;
    }

    public async Task<IBillingPriceSettings> GetCurrentAsync(CancellationToken ct)
    {
        List<SystemSetting> rows = await db.SystemSettings
            .AsNoTracking()
            .Where(s => s.Key.StartsWith(PlanPriceRules.PlanKeyPrefix))
            .ToListAsync(ct);

        bool hasOverride;
        BillingPricingSection effective = PlanPriceRules.BuildEffective(options.Value.Pricing, rows, out hasOverride);
        if (!hasOverride)
        {
            return configuredPrices;
        }

        try
        {
            return new BillingPriceSettings(effective);
        }
        catch (InvalidOperationException ex)
        {
            // Giá Admin lưu không hợp lệ (ví dụ sửa trực tiếp trong DB): dùng lại giá cấu hình để không làm hỏng bán hàng.
            logger.LogWarning(ex, "Giá gói trong SystemSettings không hợp lệ, dùng giá trong cấu hình Billing:Pricing.");
            return configuredPrices;
        }
    }
}
