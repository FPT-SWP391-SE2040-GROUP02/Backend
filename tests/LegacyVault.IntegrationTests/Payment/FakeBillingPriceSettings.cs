using LegacyVault.Application.Services;
using LegacyVault.Domain.Enums;

namespace LegacyVault.IntegrationTests.Payment;

/// <summary>
/// Bảng giá cố định cho test thuần logic (không đọc cấu hình), trùng giá SRS §8.1 phiên bản 2026-10
/// nên các test vẫn kiểm đúng số tiền hợp đồng.
/// </summary>
public sealed class FakeBillingPriceSettings : IBillingPriceSettings
{
    public string PolicyVersion
    {
        get { return "2026-10"; }
    }

    public long SlotPricePerYear
    {
        get { return 49000; }
    }

    public long GetTierPrice(ServiceTier tier, int years)
    {
        if (tier == ServiceTier.Xs)
        {
            if (years == 1)
            {
                return 199000;
            }
            if (years == 3)
            {
                return 499000;
            }
            if (years == 5)
            {
                return 799000;
            }
        }
        if (tier == ServiceTier.XsMax)
        {
            if (years == 1)
            {
                return 399000;
            }
            if (years == 3)
            {
                return 999000;
            }
            if (years == 5)
            {
                return 1599000;
            }
        }
        throw new ArgumentOutOfRangeException(nameof(tier), "Hạng hoặc số năm không có trong bảng giá.");
    }
}
