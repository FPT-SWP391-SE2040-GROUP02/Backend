using LegacyVault.Application.Exceptions;
using LegacyVault.Application.Services;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;
using Xunit;

namespace LegacyVault.IntegrationTests.Payment;

/// <summary>Luật báo giá và điều kiện tạo đơn (SRS §8.1–8.3), thuần logic, không cần database.</summary>
public sealed class BillingRulesTests
{
    private static readonly DateTimeOffset Now = new DateTimeOffset(2026, 10, 18, 12, 0, 0, TimeSpan.Zero);

    private static VaultEntitlement NewEntitlement(
        ServiceTier tier, EntitlementStatus status, DateTimeOffset? expiresAt, int extraSlots)
    {
        VaultEntitlement entitlement = new VaultEntitlement();
        entitlement.Tier = tier;
        entitlement.Status = status;
        entitlement.ServiceExpiresAt = expiresAt;
        entitlement.ExtraBeneficiarySlots = extraSlots;
        if (tier == ServiceTier.Xs)
        {
            entitlement.StorageQuotaBytes = 200 * 1048576L;
            entitlement.IncludedBeneficiarySlots = 2;
        }
        return entitlement;
    }

    private static void AssertPaymentNotAllowed(AppException exception, string expectedReason)
    {
        Assert.Equal(ErrorCode.PaymentNotAllowed, exception.Code);
        Assert.Equal(AppErrorType.Conflict, exception.Type);
        Assert.Equal(expectedReason, exception.Extensions["reason"]);
    }

    private static void AssertValidationOnField(AppException exception, string field)
    {
        Assert.Equal(ErrorCode.ValidationError, exception.Code);
        Dictionary<string, string[]> errors = (Dictionary<string, string[]>)exception.Extensions["errors"]!;
        Assert.True(errors.ContainsKey(field));
    }

    [Fact]
    public void Ac94_Addon01_PurchaseXsThreeYearsOneSlot_Is646000()
    {
        // Chuẩn bị / Hành động
        BillingQuote quote = BillingRules.QuotePurchase("XS", 3, 1);

        // Kiểm tra
        Assert.Equal(PaymentOrderKind.Purchase, quote.Kind);
        Assert.Equal(499000, quote.TierAmount);
        Assert.Equal(147000, quote.SlotAmount);
        Assert.Equal(646000, quote.TotalAmount);
        Assert.Equal(3, quote.TotalBeneficiarySlots);
        Assert.Equal(1095, quote.ServiceDays);
        Assert.Equal(200 * 1048576L, quote.StorageQuotaBytes);
    }

    [Fact]
    public void Ac94_PurchaseXsMaxFiveYears_Is1599000()
    {
        // Chuẩn bị / Hành động
        BillingQuote quote = BillingRules.QuotePurchase("XS_MAX", 5, 0);

        // Kiểm tra
        Assert.Equal(1599000, quote.TotalAmount);
        Assert.Equal(0, quote.SlotAmount);
        Assert.Equal(5, quote.TotalBeneficiarySlots);
        Assert.Equal(1825, quote.ServiceDays);
        Assert.Equal(500 * 1048576L, quote.StorageQuotaBytes);
    }

    [Theory]
    [InlineData("XS", 1, 199000)]
    [InlineData("XS", 5, 799000)]
    [InlineData("XS_MAX", 1, 399000)]
    [InlineData("XS_MAX", 3, 999000)]
    public void Sec81_PurchaseTierPrice_MatchesPriceTable(string tier, int years, long expected)
    {
        // Chuẩn bị / Hành động
        BillingQuote quote = BillingRules.QuotePurchase(tier, years, 0);

        // Kiểm tra
        Assert.Equal(expected, quote.TotalAmount);
    }

    [Fact]
    public void Addon01_Purchase_ExtraSlotsTwentyOne_Rejected()
    {
        // Chuẩn bị / Hành động
        AppException exception = Assert.Throws<AppException>(() => BillingRules.QuotePurchase("XS", 1, 21));

        // Kiểm tra
        AssertValidationOnField(exception, "extraSlots");
    }

    [Fact]
    public void Addon01_Purchase_ExtraSlotsTwenty_Allowed()
    {
        // Chuẩn bị / Hành động
        BillingQuote quote = BillingRules.QuotePurchase("XS", 1, 20);

        // Kiểm tra: 199.000 + 49.000 × 20
        Assert.Equal(1179000, quote.TotalAmount);
    }

    [Fact]
    public void Sec81_Purchase_YearsTwo_Rejected()
    {
        // Chuẩn bị / Hành động
        AppException exception = Assert.Throws<AppException>(() => BillingRules.QuotePurchase("XS", 2, 0));

        // Kiểm tra
        AssertValidationOnField(exception, "years");
    }

    [Fact]
    public void Sec81_Purchase_MissingYears_Rejected()
    {
        // Chuẩn bị / Hành động
        AppException exception = Assert.Throws<AppException>(() => BillingRules.QuotePurchase("XS", null, 0));

        // Kiểm tra
        AssertValidationOnField(exception, "years");
    }

    [Theory]
    [InlineData("FREE")]
    [InlineData("PLATINUM")]
    [InlineData("")]
    [InlineData(null)]
    public void Sec81_Purchase_TierNotSellable_Rejected(string? tier)
    {
        // Chuẩn bị / Hành động
        AppException exception = Assert.Throws<AppException>(() => BillingRules.QuotePurchase(tier, 1, 0));

        // Kiểm tra: Free không có đơn 0 đồng
        AssertValidationOnField(exception, "tier");
    }

    [Fact]
    public void Addon01_Purchase_NegativeExtraSlots_Rejected()
    {
        // Chuẩn bị / Hành động
        AppException exception = Assert.Throws<AppException>(() => BillingRules.QuotePurchase("XS", 1, -1));

        // Kiểm tra
        AssertValidationOnField(exception, "extraSlots");
    }

    [Fact]
    public void Ac95_Addon02_OneSlotWith180DaysLeft_Is24165()
    {
        // Chuẩn bị: còn đúng 180 ngày
        VaultEntitlement entitlement = NewEntitlement(ServiceTier.Xs, EntitlementStatus.PaidActive, Now.AddDays(180), 0);

        // Hành động
        BillingQuote quote = BillingRules.QuoteAddon(entitlement, 1, Now);

        // Kiểm tra: ceil(49.000 × 180 / 365) = 24.165, không đổi hạn
        Assert.Equal(PaymentOrderKind.Addon, quote.Kind);
        Assert.Equal(24165, quote.TotalAmount);
        Assert.Equal(24165, quote.SlotAmount);
        Assert.Equal(0, quote.TierAmount);
        Assert.Null(quote.Years);
        Assert.Equal(Now.AddDays(180), quote.BaseServiceExpiresAt);
    }

    [Fact]
    public void Addon02_FullYearLeft_PriceIsExactlySlotPricePerSlot()
    {
        // Chuẩn bị: còn đúng 365 ngày, mua 3 suất
        VaultEntitlement entitlement = NewEntitlement(ServiceTier.Xs, EntitlementStatus.PaidActive, Now.AddDays(365), 0);

        // Hành động
        BillingQuote quote = BillingRules.QuoteAddon(entitlement, 3, Now);

        // Kiểm tra: không dư nên không làm tròn lên
        Assert.Equal(147000, quote.TotalAmount);
    }

    [Fact]
    public void Addon02_OneSecondLeft_RoundsUpToOneDong()
    {
        // Chuẩn bị
        VaultEntitlement entitlement = NewEntitlement(ServiceTier.Xs, EntitlementStatus.PaidActive, Now.AddSeconds(1), 0);

        // Hành động
        BillingQuote quote = BillingRules.QuoteAddon(entitlement, 1, Now);

        // Kiểm tra: ceil(49.000 / 31.536.000) = 1
        Assert.Equal(1, quote.TotalAmount);
    }

    [Fact]
    public void Addon02_RemainingSecondsAreTruncatedToWholeSeconds()
    {
        // Chuẩn bị: còn 180 ngày + 0,9 giây; phần lẻ giây bị bỏ nên giá bằng khi còn đúng 180 ngày
        VaultEntitlement entitlement = NewEntitlement(
            ServiceTier.Xs, EntitlementStatus.PaidActive, Now.AddDays(180).AddMilliseconds(900), 0);

        // Hành động
        BillingQuote quote = BillingRules.QuoteAddon(entitlement, 1, Now);

        // Kiểm tra
        Assert.Equal(24165, quote.TotalAmount);
    }

    [Fact]
    public void Addon01_Addon_TotalSlotsIncludeExistingExtraSlots()
    {
        // Chuẩn bị: XS (2 suất kèm) đã mua thêm 1 suất, mua tiếp 2 suất
        VaultEntitlement entitlement = NewEntitlement(ServiceTier.Xs, EntitlementStatus.PaidActive, Now.AddDays(100), 1);

        // Hành động
        BillingQuote quote = BillingRules.QuoteAddon(entitlement, 2, Now);

        // Kiểm tra
        Assert.Equal(5, quote.TotalBeneficiarySlots);
        Assert.Equal(2, quote.IncludedBeneficiarySlots);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(21)]
    public void Addon01_Addon_ExtraSlotsOutOfRange_Rejected(int extraSlots)
    {
        // Chuẩn bị
        VaultEntitlement entitlement = NewEntitlement(ServiceTier.Xs, EntitlementStatus.PaidActive, Now.AddDays(100), 0);

        // Hành động
        AppException exception = Assert.Throws<AppException>(() => BillingRules.QuoteAddon(entitlement, extraSlots, Now));

        // Kiểm tra
        AssertValidationOnField(exception, "extraSlots");
    }

    [Fact]
    public void Addon03_OrderExpiry_ServiceEndsBeforeFifteenMinutes_UsesServiceEnd()
    {
        // Chuẩn bị: kỳ hết sau 5 phút
        DateTimeOffset serviceEnd = Now.AddMinutes(5);

        // Hành động
        DateTimeOffset expiresAt = BillingRules.ComputeOrderExpiresAt(PaymentOrderKind.Addon, Now, serviceEnd);

        // Kiểm tra
        Assert.Equal(serviceEnd, expiresAt);
    }

    [Fact]
    public void Addon03_OrderExpiry_ServiceEndsAfterFifteenMinutes_UsesFifteenMinutes()
    {
        // Chuẩn bị / Hành động
        DateTimeOffset expiresAt = BillingRules.ComputeOrderExpiresAt(PaymentOrderKind.Addon, Now, Now.AddDays(30));

        // Kiểm tra
        Assert.Equal(Now.AddMinutes(15), expiresAt);
    }

    [Fact]
    public void Sec83_PurchaseOrderExpiry_IsFifteenMinutes()
    {
        // Chuẩn bị / Hành động
        DateTimeOffset expiresAt = BillingRules.ComputeOrderExpiresAt(PaymentOrderKind.Purchase, Now, null);

        // Kiểm tra
        Assert.Equal(Now.AddMinutes(15), expiresAt);
    }

    [Fact]
    public void Sec83_DetermineKind_FreeEntitlement_IsPurchase()
    {
        // Chuẩn bị
        VaultEntitlement entitlement = NewEntitlement(ServiceTier.Free, EntitlementStatus.Free, null, 0);

        // Hành động / Kiểm tra
        Assert.Equal(PaymentOrderKind.Purchase, BillingRules.DetermineKind(entitlement, Now));
    }

    [Fact]
    public void Sec83_DetermineKind_PaidActiveNotExpired_IsAddon()
    {
        // Chuẩn bị
        VaultEntitlement entitlement = NewEntitlement(ServiceTier.Xs, EntitlementStatus.PaidActive, Now.AddDays(10), 0);

        // Hành động / Kiểm tra
        Assert.Equal(PaymentOrderKind.Addon, BillingRules.DetermineKind(entitlement, Now));
    }

    [Theory]
    [InlineData(EntitlementStatus.PaidActive, -1)]
    [InlineData(EntitlementStatus.GraceReadOnly, 30)]
    [InlineData(EntitlementStatus.ExpiredReview, -300)]
    public void Sec83_DetermineKind_NeedsRenewal_NotSupported(EntitlementStatus status, int daysUntilExpiry)
    {
        // Chuẩn bị: hết hạn, ân hạn hoặc chờ rà soát đều thuộc RENEWAL
        VaultEntitlement entitlement = NewEntitlement(ServiceTier.Xs, status, Now.AddDays(daysUntilExpiry), 0);

        // Hành động
        AppException exception = Assert.Throws<AppException>(() => BillingRules.DetermineKind(entitlement, Now));

        // Kiểm tra
        AssertPaymentNotAllowed(exception, "RENEWAL_NOT_SUPPORTED");
    }

    [Fact]
    public void Sec83_PaidActiveWithoutExpiry_NotTreatedAsAddon()
    {
        // Chuẩn bị: dữ liệu bất thường, không có hạn thì không bán thêm suất
        VaultEntitlement entitlement = NewEntitlement(ServiceTier.Xs, EntitlementStatus.PaidActive, null, 0);

        // Hành động
        AppException exception = Assert.Throws<AppException>(() => BillingRules.DetermineKind(entitlement, Now));

        // Kiểm tra
        AssertPaymentNotAllowed(exception, "RENEWAL_NOT_SUPPORTED");
    }

    [Theory]
    [InlineData(89, true)]
    [InlineData(90, true)]
    [InlineData(91, false)]
    public void Sec83_OwnerEkycFreshness_NinetyDays(int daysAgo, bool expectedFresh)
    {
        // Chuẩn bị
        EkycVerification verification = new EkycVerification();
        verification.Status = EkycStatus.Passed;
        verification.CompletedAt = Now.AddDays(-daysAgo);

        // Hành động / Kiểm tra
        Assert.Equal(expectedFresh, BillingRules.IsOwnerEkycFresh(verification, Now));
    }

    [Fact]
    public void Sec83_OwnerEkycFreshness_NullFailedOrNoCompletionTime_NotFresh()
    {
        // Chuẩn bị
        EkycVerification failed = new EkycVerification();
        failed.Status = EkycStatus.Failed;
        failed.CompletedAt = Now;
        EkycVerification noTime = new EkycVerification();
        noTime.Status = EkycStatus.Passed;
        noTime.CompletedAt = null;

        // Hành động / Kiểm tra
        Assert.False(BillingRules.IsOwnerEkycFresh(null, Now));
        Assert.False(BillingRules.IsOwnerEkycFresh(failed, Now));
        Assert.False(BillingRules.IsOwnerEkycFresh(noTime, Now));
    }

    private static PaymentOrder NewOrder(PaymentOrderStatus status, DateTimeOffset expiresAt)
    {
        PaymentOrder order = new PaymentOrder();
        order.Kind = PaymentOrderKind.Purchase;
        order.Tier = ServiceTier.Xs;
        order.Years = 3;
        order.ExtraSlots = 1;
        order.Status = status;
        order.ExpiresAt = expiresAt;
        return order;
    }

    [Theory]
    [InlineData(PaymentOrderStatus.Pending, -1, true)]
    [InlineData(PaymentOrderStatus.Pending, 0, true)]
    [InlineData(PaymentOrderStatus.Pending, 1, false)]
    [InlineData(PaymentOrderStatus.Paid, -60, false)]
    [InlineData(PaymentOrderStatus.Cancelled, -60, false)]
    [InlineData(PaymentOrderStatus.Expired, -60, false)]
    public void D7_IsExpired_OnlyPendingOrderAtOrPastExpiry(PaymentOrderStatus status, int secondsUntilExpiry, bool expected)
    {
        // Chuẩn bị
        PaymentOrder order = NewOrder(status, Now.AddSeconds(secondsUntilExpiry));

        // Hành động / Kiểm tra
        Assert.Equal(expected, BillingRules.IsExpired(order, Now));
    }

    [Fact]
    public void Sec83_IsSameSelection_ComparesKindTierYearsAndExtraSlots()
    {
        // Chuẩn bị
        PaymentOrder order = NewOrder(PaymentOrderStatus.Pending, Now.AddMinutes(15));
        BillingQuote same = BillingRules.QuotePurchase("XS", 3, 1);
        BillingQuote otherTier = BillingRules.QuotePurchase("XS_MAX", 3, 1);
        BillingQuote otherYears = BillingRules.QuotePurchase("XS", 1, 1);
        BillingQuote otherSlots = BillingRules.QuotePurchase("XS", 3, 2);
        VaultEntitlement entitlement = NewEntitlement(ServiceTier.Xs, EntitlementStatus.PaidActive, Now.AddDays(100), 0);
        BillingQuote addon = BillingRules.QuoteAddon(entitlement, 1, Now);

        // Hành động / Kiểm tra
        Assert.True(BillingRules.IsSameSelection(order, same));
        Assert.False(BillingRules.IsSameSelection(order, otherTier));
        Assert.False(BillingRules.IsSameSelection(order, otherYears));
        Assert.False(BillingRules.IsSameSelection(order, otherSlots));
        Assert.False(BillingRules.IsSameSelection(order, addon));
    }

    private static PaymentOrder NewPurchaseOrder(string tierText, int years, int extraSlots)
    {
        BillingQuote quote = BillingRules.QuotePurchase(tierText, years, extraSlots);
        PaymentOrder order = new PaymentOrder();
        order.Kind = quote.Kind;
        order.Tier = quote.Tier;
        order.Years = quote.Years;
        order.ExtraSlots = quote.ExtraSlots;
        order.StorageQuotaBytes = quote.StorageQuotaBytes;
        order.IncludedBeneficiarySlots = quote.IncludedBeneficiarySlots;
        order.Status = PaymentOrderStatus.Pending;
        return order;
    }

    private static PaymentOrder NewAddonOrder(VaultEntitlement entitlement, int extraSlots)
    {
        BillingQuote quote = BillingRules.QuoteAddon(entitlement, extraSlots, Now);
        PaymentOrder order = new PaymentOrder();
        order.Kind = quote.Kind;
        order.Tier = quote.Tier;
        order.ExtraSlots = quote.ExtraSlots;
        order.BaseServiceExpiresAt = quote.BaseServiceExpiresAt;
        order.Status = PaymentOrderStatus.Pending;
        return order;
    }

    private static void AssertNotPayable(AppException exception, string expectedReason)
    {
        Assert.Equal(ErrorCode.PaymentOrderNotPayable, exception.Code);
        Assert.Equal(AppErrorType.Conflict, exception.Type);
        Assert.Equal(expectedReason, exception.Extensions["reason"]);
    }

    [Fact]
    public void Ac90_Pay08_ApplyPurchase_SetsTierQuotaSlotsAndPeriodFromPaymentTime()
    {
        // Chuẩn bị: kho Free, đơn XS 3 năm + 1 suất
        VaultEntitlement entitlement = NewEntitlement(ServiceTier.Free, EntitlementStatus.Free, null, 0);
        PaymentOrder order = NewPurchaseOrder("XS", 3, 1);

        // Hành động
        BillingRules.ApplyPurchase(entitlement, order, Now);

        // Kiểm tra: kỳ = 3 × 365 ngày kể từ lúc trả tiền
        Assert.Equal(ServiceTier.Xs, entitlement.Tier);
        Assert.Equal(EntitlementStatus.PaidActive, entitlement.Status);
        Assert.Equal(200 * 1048576L, entitlement.StorageQuotaBytes);
        Assert.Equal(2, entitlement.IncludedBeneficiarySlots);
        Assert.Equal(1, entitlement.ExtraBeneficiarySlots);
        Assert.Equal(Now.AddDays(1095), entitlement.ServiceExpiresAt);
        Assert.Equal(Now, entitlement.UpdatedAt);
    }

    [Fact]
    public void Ac94_ApplyPurchase_XsMaxFiveYears_Is500MiBFiveSlots1825Days()
    {
        // Chuẩn bị
        VaultEntitlement entitlement = NewEntitlement(ServiceTier.Free, EntitlementStatus.Free, null, 0);
        PaymentOrder order = NewPurchaseOrder("XS_MAX", 5, 0);

        // Hành động
        BillingRules.ApplyPurchase(entitlement, order, Now);

        // Kiểm tra
        Assert.Equal(ServiceTier.XsMax, entitlement.Tier);
        Assert.Equal(500 * 1048576L, entitlement.StorageQuotaBytes);
        Assert.Equal(5, entitlement.IncludedBeneficiarySlots);
        Assert.Equal(0, entitlement.ExtraBeneficiarySlots);
        Assert.Equal(Now.AddDays(1825), entitlement.ServiceExpiresAt);
    }

    [Theory]
    [InlineData(EntitlementStatus.PaidActive)]
    [InlineData(EntitlementStatus.GraceReadOnly)]
    [InlineData(EntitlementStatus.ExpiredReview)]
    public void D8_ApplyPurchase_EntitlementNoLongerFree_NotPayable(EntitlementStatus status)
    {
        // Chuẩn bị: quyền dịch vụ đã đổi từ lúc tạo đơn
        VaultEntitlement entitlement = NewEntitlement(ServiceTier.Xs, status, Now.AddDays(10), 0);
        PaymentOrder order = NewPurchaseOrder("XS_MAX", 1, 0);

        // Hành động
        AppException exception = Assert.Throws<AppException>(() => BillingRules.ApplyPurchase(entitlement, order, Now));

        // Kiểm tra: không ghi gì vào quyền dịch vụ
        AssertNotPayable(exception, "ENTITLEMENT_CHANGED");
        Assert.Equal(ServiceTier.Xs, entitlement.Tier);
        Assert.Equal(0, entitlement.ExtraBeneficiarySlots);
    }

    [Fact]
    public void Ac95_ApplyAddon_AddsSlotsAndKeepsExpiry()
    {
        // Chuẩn bị: XS còn 180 ngày, đã có 1 suất thêm, mua thêm 2
        DateTimeOffset expiry = Now.AddDays(180);
        VaultEntitlement entitlement = NewEntitlement(ServiceTier.Xs, EntitlementStatus.PaidActive, expiry, 1);
        PaymentOrder order = NewAddonOrder(entitlement, 2);

        // Hành động
        BillingRules.ApplyAddon(entitlement, order, Now.AddMinutes(3));

        // Kiểm tra: hạn không đổi
        Assert.Equal(3, entitlement.ExtraBeneficiarySlots);
        Assert.Equal(expiry, entitlement.ServiceExpiresAt);
        Assert.Equal(ServiceTier.Xs, entitlement.Tier);
        Assert.Equal(EntitlementStatus.PaidActive, entitlement.Status);
    }

    [Fact]
    public void Addon03_ApplyAddon_ExpiryChangedSinceOrder_NotPayable()
    {
        // Chuẩn bị: kỳ được gia hạn sau khi tạo đơn
        VaultEntitlement entitlement = NewEntitlement(ServiceTier.Xs, EntitlementStatus.PaidActive, Now.AddDays(180), 0);
        PaymentOrder order = NewAddonOrder(entitlement, 1);
        entitlement.ServiceExpiresAt = Now.AddDays(900);

        // Hành động
        AppException exception = Assert.Throws<AppException>(() => BillingRules.ApplyAddon(entitlement, order, Now));

        // Kiểm tra
        AssertNotPayable(exception, "ENTITLEMENT_CHANGED");
        Assert.Equal(0, entitlement.ExtraBeneficiarySlots);
    }

    [Fact]
    public void Addon03_ApplyAddon_PeriodAlreadyEnded_NotPayable()
    {
        // Chuẩn bị: tiền tới sau khi kỳ hết
        VaultEntitlement entitlement = NewEntitlement(ServiceTier.Xs, EntitlementStatus.PaidActive, Now.AddMinutes(5), 0);
        PaymentOrder order = NewAddonOrder(entitlement, 1);

        // Hành động
        AppException exception = Assert.Throws<AppException>(
            () => BillingRules.ApplyAddon(entitlement, order, Now.AddMinutes(6)));

        // Kiểm tra
        AssertNotPayable(exception, "ENTITLEMENT_CHANGED");
        Assert.Equal(0, entitlement.ExtraBeneficiarySlots);
    }

    [Theory]
    [InlineData(EntitlementStatus.GraceReadOnly)]
    [InlineData(EntitlementStatus.ExpiredReview)]
    [InlineData(EntitlementStatus.Free)]
    public void Addon03_ApplyAddon_EntitlementNoLongerPaidActive_NotPayable(EntitlementStatus status)
    {
        // Chuẩn bị
        VaultEntitlement entitlement = NewEntitlement(ServiceTier.Xs, EntitlementStatus.PaidActive, Now.AddDays(100), 0);
        PaymentOrder order = NewAddonOrder(entitlement, 1);
        entitlement.Status = status;

        // Hành động
        AppException exception = Assert.Throws<AppException>(() => BillingRules.ApplyAddon(entitlement, order, Now));

        // Kiểm tra
        AssertNotPayable(exception, "ENTITLEMENT_CHANGED");
    }

    [Fact]
    public void Addon03_ApplyAddon_TierChangedSinceOrder_NotPayable()
    {
        // Chuẩn bị
        VaultEntitlement entitlement = NewEntitlement(ServiceTier.Xs, EntitlementStatus.PaidActive, Now.AddDays(100), 0);
        PaymentOrder order = NewAddonOrder(entitlement, 1);
        entitlement.Tier = ServiceTier.XsMax;

        // Hành động
        AppException exception = Assert.Throws<AppException>(() => BillingRules.ApplyAddon(entitlement, order, Now));

        // Kiểm tra
        AssertNotPayable(exception, "ENTITLEMENT_CHANGED");
    }

    [Fact]
    public void Pay04_NewPaymentCode_HasPrefixLengthAndAllowedCharactersOnly()
    {
        // Chuẩn bị / Hành động
        HashSet<string> codes = new HashSet<string>();
        for (int i = 0; i < 1000; i++)
        {
            codes.Add(BillingRules.NewPaymentCode());
        }

        // Kiểm tra: LV + 8 ký tự, không có 0/O/1/I, không trùng nhau
        Assert.Equal(1000, codes.Count);
        foreach (string code in codes)
        {
            Assert.Equal(10, code.Length);
            Assert.StartsWith("LV", code);
            for (int i = 2; i < code.Length; i++)
            {
                Assert.Contains(code[i], BillingRules.PaymentCodeAlphabet);
            }
        }
        Assert.DoesNotContain('0', BillingRules.PaymentCodeAlphabet);
        Assert.DoesNotContain('O', BillingRules.PaymentCodeAlphabet);
        Assert.DoesNotContain('1', BillingRules.PaymentCodeAlphabet);
        Assert.DoesNotContain('I', BillingRules.PaymentCodeAlphabet);
    }

    [Theory]
    [InlineData(PlanStatus.DeathCaseOpen)]
    [InlineData(PlanStatus.HandoverInProgress)]
    [InlineData(PlanStatus.Blocked)]
    public void Sec83_PlanLocked_RejectsPayment(PlanStatus status)
    {
        // Chuẩn bị
        Vault vault = new Vault();
        vault.Status = VaultStatus.Open;
        vault.Plan = new HandoverPlan();
        vault.Plan.Status = status;

        // Hành động
        AppException exception = Assert.Throws<AppException>(() => BillingRules.EnsureVaultAndPlanAllowPayment(vault));

        // Kiểm tra
        AssertPaymentNotAllowed(exception, "PLAN_LOCKED");
    }

    [Theory]
    [InlineData(PlanStatus.Draft)]
    [InlineData(PlanStatus.Active)]
    [InlineData(PlanStatus.CheckinOverdue)]
    [InlineData(PlanStatus.AwaitingCheck)]
    public void Sec83_PlanNotLocked_AllowsPayment(PlanStatus status)
    {
        // Chuẩn bị
        Vault vault = new Vault();
        vault.Status = VaultStatus.Open;
        vault.Plan = new HandoverPlan();
        vault.Plan.Status = status;

        // Hành động / Kiểm tra: không ném lỗi
        BillingRules.EnsureVaultAndPlanAllowPayment(vault);
    }

    [Fact]
    public void Ac104_ClosedVault_RejectsPayment()
    {
        // Chuẩn bị
        Vault vault = new Vault();
        vault.Status = VaultStatus.Closed;
        vault.Plan = new HandoverPlan();
        vault.Plan.Status = PlanStatus.Draft;

        // Hành động
        AppException exception = Assert.Throws<AppException>(() => BillingRules.EnsureVaultAndPlanAllowPayment(vault));

        // Kiểm tra
        AssertPaymentNotAllowed(exception, "VAULT_CLOSED");
    }
}
