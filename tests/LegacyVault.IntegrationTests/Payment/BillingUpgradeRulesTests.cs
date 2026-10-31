using LegacyVault.Application.Exceptions;
using LegacyVault.Application.Services;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;
using Xunit;

namespace LegacyVault.IntegrationTests.Payment;

/// <summary>
/// Luật nâng hạng giữa kỳ XS → XS_MAX: trả phần chênh giá theo thời gian còn lại, giữ nguyên hạn. Thuần logic, không cần database.
/// </summary>
public sealed class BillingUpgradeRulesTests
{
    private static readonly IBillingPriceSettings Prices = new FakeBillingPriceSettings();

    private static readonly DateTimeOffset Now = new DateTimeOffset(2026, 10, 18, 12, 0, 0, TimeSpan.Zero);

    private const long SecondsPerDay = 86400;

    private static VaultEntitlement NewXsEntitlement(DateTimeOffset? expiresAt, int extraSlots)
    {
        VaultEntitlement entitlement = new VaultEntitlement();
        entitlement.Tier = ServiceTier.Xs;
        entitlement.Status = EntitlementStatus.PaidActive;
        entitlement.ServiceExpiresAt = expiresAt;
        entitlement.ExtraBeneficiarySlots = extraSlots;
        entitlement.StorageQuotaBytes = 200 * 1048576L;
        entitlement.IncludedBeneficiarySlots = 2;
        return entitlement;
    }

    [Theory]
    // Mua XS 1 năm, còn nửa năm: chênh 200.000 đ × 1/2
    [InlineData(1, 15768000L, 100000L)]
    // Mua XS 3 năm, vừa mua xong (còn đủ 1.095 ngày): trả đủ chênh 500.000 đ
    [InlineData(3, 1095 * SecondsPerDay, 500000L)]
    // Mua XS 3 năm, đã dùng 1,5 năm (còn 547,5 ngày): một nửa của 500.000 đ
    [InlineData(3, 47304000L, 250000L)]
    // Mua XS 5 năm, còn 1 năm: chênh 800.000 đ × 365/1.825
    [InlineData(5, 365 * SecondsPerDay, 160000L)]
    // Mua XS 3 năm, còn 10 ngày: 500.000 × 864.000 / 94.608.000 = 4.566,5 → làm tròn lên 4.567
    [InlineData(3, 10 * SecondsPerDay, 4567L)]
    public void Upgrade_PriceIsPriceDifferenceProratedByRemainingTimeOfPaidTerm(
        int paidYears, long remainingSeconds, long expectedPrice)
    {
        // Chuẩn bị
        VaultEntitlement entitlement = NewXsEntitlement(Now.AddSeconds(remainingSeconds), 0);

        // Hành động
        BillingQuote quote = BillingRules.QuoteUpgrade(Prices, entitlement, paidYears, 0, Now);

        // Kiểm tra
        Assert.Equal(expectedPrice, quote.TotalAmount);
        Assert.Equal(expectedPrice, quote.TierAmount);
        Assert.Equal(0, quote.SlotAmount);
    }

    [Fact]
    public void Upgrade_NoPaidPurchaseOrder_FallsBackToOneYearPrices()
    {
        // Chuẩn bị: kho tạo bằng SQL dev, không có đơn mua; còn đúng 365 ngày
        VaultEntitlement entitlement = NewXsEntitlement(Now.AddDays(365), 0);

        // Hành động
        BillingQuote quote = BillingRules.QuoteUpgrade(Prices, entitlement, null, 0, Now);

        // Kiểm tra: chênh giá 1 năm = 399.000 − 199.000
        Assert.Equal(200000, quote.TotalAmount);
    }

    [Fact]
    public void Upgrade_RemainingLongerThanPaidTerm_IsCappedAtFullPriceDifference()
    {
        // Chuẩn bị: kỳ mua 1 năm nhưng hạn còn 2 năm (ví dụ Admin kéo dài); không tính quá chênh giá cả kỳ
        VaultEntitlement entitlement = NewXsEntitlement(Now.AddDays(730), 0);

        // Hành động
        BillingQuote quote = BillingRules.QuoteUpgrade(Prices, entitlement, 1, 0, Now);

        // Kiểm tra
        Assert.Equal(200000, quote.TotalAmount);
    }

    [Fact]
    public void Upgrade_QuoteDescribesXsMaxAndKeepsExpiryAndExtraSlots()
    {
        // Chuẩn bị: XS còn 100 ngày, đã mua thêm 1 suất
        DateTimeOffset expiresAt = Now.AddDays(100);
        VaultEntitlement entitlement = NewXsEntitlement(expiresAt, 1);

        // Hành động
        BillingQuote quote = BillingRules.QuoteUpgrade(Prices, entitlement, 1, 0, Now);

        // Kiểm tra: XS_MAX 500 MiB, 5 suất kèm + 1 suất thêm, hạn giữ nguyên, không có số năm hay số ngày kỳ mới
        Assert.Equal(PaymentOrderKind.Upgrade, quote.Kind);
        Assert.Equal(ServiceTier.XsMax, quote.Tier);
        Assert.Null(quote.Years);
        Assert.Null(quote.ServiceDays);
        Assert.Equal(0, quote.ExtraSlots);
        Assert.Equal(500 * 1048576L, quote.StorageQuotaBytes);
        Assert.Equal(5, quote.IncludedBeneficiarySlots);
        Assert.Equal(6, quote.TotalBeneficiarySlots);
        Assert.Equal(expiresAt, quote.BaseServiceExpiresAt);
    }

    [Fact]
    public void Upgrade_WithExtraSlots_IsRejected()
    {
        // Chuẩn bị
        VaultEntitlement entitlement = NewXsEntitlement(Now.AddDays(100), 0);

        // Hành động + kiểm tra: nâng hạng không kèm mua thêm suất
        Assert.Throws<AppException>(() => BillingRules.QuoteUpgrade(Prices, entitlement, 1, 1, Now));
    }

    [Fact]
    public void Upgrade_ExpiredPeriod_IsNotSupported()
    {
        // Chuẩn bị: hạn đã qua
        VaultEntitlement entitlement = NewXsEntitlement(Now.AddSeconds(-1), 0);

        // Hành động + kiểm tra
        Assert.Throws<AppException>(() => BillingRules.QuoteUpgrade(Prices, entitlement, 1, 0, Now));
    }

    [Theory]
    [InlineData("XS_MAX", 0, true)]
    [InlineData("xs_max", 0, true)]
    [InlineData(" XS_MAX ", 0, true)]
    // Kèm mua thêm suất: là ADDON, tier bị bỏ qua như hợp đồng cũ
    [InlineData("XS_MAX", 1, false)]
    [InlineData("XS", 0, false)]
    [InlineData("", 0, false)]
    [InlineData(null, 0, false)]
    public void DetermineKind_PaidXsActive_OnlyXsMaxWithoutExtraSlotsIsUpgrade(
        string? tier, int extraSlots, bool expectUpgrade)
    {
        // Chuẩn bị
        VaultEntitlement entitlement = NewXsEntitlement(Now.AddDays(100), 0);

        // Hành động
        PaymentOrderKind kind = BillingRules.DetermineKind(entitlement, tier, extraSlots, Now);

        // Kiểm tra: các lựa chọn khác giữ nguyên hành vi cũ là mua thêm suất
        if (expectUpgrade)
        {
            Assert.Equal(PaymentOrderKind.Upgrade, kind);
        }
        else
        {
            Assert.Equal(PaymentOrderKind.Addon, kind);
        }
    }

    [Fact]
    public void DetermineKind_XsMaxVaultChoosingXsMax_StaysAddon()
    {
        // Chuẩn bị: đã ở hạng cao nhất, không có gì để nâng
        VaultEntitlement entitlement = NewXsEntitlement(Now.AddDays(100), 0);
        entitlement.Tier = ServiceTier.XsMax;

        // Hành động + kiểm tra
        Assert.Equal(PaymentOrderKind.Addon, BillingRules.DetermineKind(entitlement, "XS_MAX", 0, Now));
    }

    [Fact]
    public void DetermineKind_FreeVaultChoosingXsMax_IsPurchase()
    {
        // Chuẩn bị
        VaultEntitlement entitlement = new VaultEntitlement();
        entitlement.Tier = ServiceTier.Free;
        entitlement.Status = EntitlementStatus.Free;

        // Hành động + kiểm tra
        Assert.Equal(PaymentOrderKind.Purchase, BillingRules.DetermineKind(entitlement, "XS_MAX", 0, Now));
    }

    [Fact]
    public void DetermineKind_ExpiredPaidVault_IsRenewalNotUpgrade()
    {
        // Chuẩn bị
        VaultEntitlement entitlement = NewXsEntitlement(Now.AddDays(-1), 0);

        // Hành động + kiểm tra: hết hạn thì không nâng hạng giữa kỳ được, chỉ gia hạn
        Assert.Equal(PaymentOrderKind.Renewal, BillingRules.DetermineKind(entitlement, "XS_MAX", 0, Now));
    }

    private static PaymentOrder NewUpgradeOrder(DateTimeOffset baseExpiresAt)
    {
        PaymentOrder order = new PaymentOrder();
        order.Kind = PaymentOrderKind.Upgrade;
        order.Tier = ServiceTier.XsMax;
        order.StorageQuotaBytes = 500 * 1048576L;
        order.IncludedBeneficiarySlots = 5;
        order.BaseServiceExpiresAt = baseExpiresAt;
        return order;
    }

    [Fact]
    public void ApplyUpgrade_ChangesTierQuotaAndIncludedSlots_KeepsExpiryAndExtraSlots()
    {
        // Chuẩn bị
        DateTimeOffset expiresAt = Now.AddDays(100);
        VaultEntitlement entitlement = NewXsEntitlement(expiresAt, 2);
        PaymentOrder order = NewUpgradeOrder(expiresAt);

        // Hành động
        BillingRules.ApplyUpgrade(entitlement, order, Now);

        // Kiểm tra
        Assert.Equal(ServiceTier.XsMax, entitlement.Tier);
        Assert.Equal(500 * 1048576L, entitlement.StorageQuotaBytes);
        Assert.Equal(5, entitlement.IncludedBeneficiarySlots);
        Assert.Equal(2, entitlement.ExtraBeneficiarySlots);
        Assert.Equal(expiresAt, entitlement.ServiceExpiresAt);
        Assert.Equal(EntitlementStatus.PaidActive, entitlement.Status);
    }

    [Fact]
    public void ApplyUpgrade_ExpiryChangedSinceOrder_IsRejected()
    {
        // Chuẩn bị: hạn của kho khác hạn lúc tạo đơn
        VaultEntitlement entitlement = NewXsEntitlement(Now.AddDays(200), 0);
        PaymentOrder order = NewUpgradeOrder(Now.AddDays(100));

        // Hành động + kiểm tra
        Assert.Throws<AppException>(() => BillingRules.ApplyUpgrade(entitlement, order, Now));
        Assert.Equal(ServiceTier.Xs, entitlement.Tier);
    }

    [Fact]
    public void ApplyUpgrade_AlreadyXsMax_IsRejected()
    {
        // Chuẩn bị: kho đã lên XS_MAX bằng đơn khác
        DateTimeOffset expiresAt = Now.AddDays(100);
        VaultEntitlement entitlement = NewXsEntitlement(expiresAt, 0);
        entitlement.Tier = ServiceTier.XsMax;
        PaymentOrder order = NewUpgradeOrder(expiresAt);

        // Hành động + kiểm tra
        Assert.Throws<AppException>(() => BillingRules.ApplyUpgrade(entitlement, order, Now));
    }

    [Fact]
    public void ApplyUpgrade_PeriodAlreadyEnded_IsRejected()
    {
        // Chuẩn bị: tiền tới sau khi kỳ đã hết
        DateTimeOffset expiresAt = Now.AddDays(1);
        VaultEntitlement entitlement = NewXsEntitlement(expiresAt, 0);
        PaymentOrder order = NewUpgradeOrder(expiresAt);

        // Hành động + kiểm tra
        Assert.Throws<AppException>(() => BillingRules.ApplyUpgrade(entitlement, order, expiresAt.AddSeconds(1)));
    }

    [Fact]
    public void OrderExpiry_Upgrade_IsEarlierOfFifteenMinutesAndEndOfCurrentTerm()
    {
        // Hành động
        DateTimeOffset normal = BillingRules.ComputeOrderExpiresAt(PaymentOrderKind.Upgrade, Now, Now.AddDays(10));
        DateTimeOffset nearEnd = BillingRules.ComputeOrderExpiresAt(PaymentOrderKind.Upgrade, Now, Now.AddMinutes(5));

        // Kiểm tra: giống đơn mua thêm suất
        Assert.Equal(Now.AddMinutes(15), normal);
        Assert.Equal(Now.AddMinutes(5), nearEnd);
    }
}
