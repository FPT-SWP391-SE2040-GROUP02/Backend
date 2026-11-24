using LegacyVault.Application.Exceptions;
using LegacyVault.Application.Services;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;
using Xunit;

namespace LegacyVault.IntegrationTests.Payment;

/// <summary>Luật báo giá và điều kiện tạo đơn (SRS §8.1–8.3), thuần logic, không cần database.</summary>
public sealed class BillingRulesTests
{
    private static readonly IBillingPriceSettings Prices = new FakeBillingPriceSettings();

    private static readonly DateTimeOffset Now = new DateTimeOffset(2026, 10, 18, 12, 0, 0, TimeSpan.Zero);

    private static VaultEntitlement NewEntitlement(
        ServiceTier tier, EntitlementStatus status, DateTimeOffset? expiresAt)
    {
        VaultEntitlement entitlement = new VaultEntitlement();
        entitlement.Tier = tier;
        entitlement.Status = status;
        entitlement.ServiceExpiresAt = expiresAt;
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
    public void Ac94_PurchaseXsThreeYears_Is499000()
    {
        // Chuẩn bị / Hành động
        BillingQuote quote = BillingRules.QuotePurchase(Prices, "XS", 3);

        // Kiểm tra
        Assert.Equal(PaymentOrderKind.Purchase, quote.Kind);
        Assert.Equal(499000, quote.TierAmount);
        Assert.Equal(499000, quote.TotalAmount);
        Assert.Equal(2, quote.IncludedBeneficiarySlots);
        Assert.Equal(1095, quote.ServiceDays);
        Assert.Equal(200 * 1048576L, quote.StorageQuotaBytes);
    }

    [Fact]
    public void Ac94_PurchaseXsMaxFiveYears_Is1599000()
    {
        // Chuẩn bị / Hành động
        BillingQuote quote = BillingRules.QuotePurchase(Prices, "XS_MAX", 5);

        // Kiểm tra
        Assert.Equal(1599000, quote.TotalAmount);
        Assert.Equal(5, quote.IncludedBeneficiarySlots);
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
        BillingQuote quote = BillingRules.QuotePurchase(Prices, tier, years);

        // Kiểm tra
        Assert.Equal(expected, quote.TotalAmount);
    }

    [Fact]
    public void Sec81_Purchase_YearsTwo_Rejected()
    {
        // Chuẩn bị / Hành động
        AppException exception = Assert.Throws<AppException>(() => BillingRules.QuotePurchase(Prices, "XS", 2));

        // Kiểm tra
        AssertValidationOnField(exception, "years");
    }

    [Fact]
    public void Sec81_Purchase_MissingYears_Rejected()
    {
        // Chuẩn bị / Hành động
        AppException exception = Assert.Throws<AppException>(() => BillingRules.QuotePurchase(Prices, "XS", null));

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
        AppException exception = Assert.Throws<AppException>(() => BillingRules.QuotePurchase(Prices, tier, 1));

        // Kiểm tra: Free không có đơn 0 đồng
        AssertValidationOnField(exception, "tier");
    }

    [Fact]
    public void Upgrade_OrderExpiry_ServiceEndsBeforeFifteenMinutes_UsesServiceEnd()
    {
        // Chuẩn bị: kỳ hết sau 5 phút
        DateTimeOffset serviceEnd = Now.AddMinutes(5);

        // Hành động
        DateTimeOffset expiresAt = BillingRules.ComputeOrderExpiresAt(PaymentOrderKind.Upgrade, Now, serviceEnd);

        // Kiểm tra
        Assert.Equal(serviceEnd, expiresAt);
    }

    [Fact]
    public void Upgrade_OrderExpiry_ServiceEndsAfterFifteenMinutes_UsesFifteenMinutes()
    {
        // Chuẩn bị / Hành động
        DateTimeOffset expiresAt = BillingRules.ComputeOrderExpiresAt(PaymentOrderKind.Upgrade, Now, Now.AddDays(30));

        // Kiểm tra
        Assert.Equal(Now.AddMinutes(15), expiresAt);
    }

    [Fact]
    public void OrderExpiry_UsesAdminConfiguredLifetime()
    {
        // Chuẩn bị / Hành động: Admin đặt hạn đơn 20 phút
        DateTimeOffset purchase = BillingRules.ComputeOrderExpiresAt(
            PaymentOrderKind.Purchase, Now, null, TimeSpan.FromMinutes(20));
        DateTimeOffset upgradeNearEnd = BillingRules.ComputeOrderExpiresAt(
            PaymentOrderKind.Upgrade, Now, Now.AddMinutes(5), TimeSpan.FromMinutes(20));

        // Kiểm tra: vẫn lấy mốc sớm hơn khi kỳ sắp hết
        Assert.Equal(Now.AddMinutes(20), purchase);
        Assert.Equal(Now.AddMinutes(5), upgradeNearEnd);
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
        VaultEntitlement entitlement = NewEntitlement(ServiceTier.Free, EntitlementStatus.Free, null);

        // Hành động / Kiểm tra
        Assert.Equal(PaymentOrderKind.Purchase, BillingRules.DetermineKind(Prices, entitlement, "XS", false, Now));
    }

    [Fact]
    public void Sec83_DetermineKind_PaidActiveNotExpired_WithRenewFlag_IsRenewal()
    {
        // Chuẩn bị
        VaultEntitlement entitlement = NewEntitlement(ServiceTier.Xs, EntitlementStatus.PaidActive, Now.AddDays(10));

        // Hành động / Kiểm tra: gia hạn sớm
        Assert.Equal(PaymentOrderKind.Renewal, BillingRules.DetermineKind(Prices, entitlement, "XS", true, Now));
    }

    [Fact]
    public void Sec83_DetermineKind_PaidActiveNotExpired_HigherPlan_IsUpgrade()
    {
        // Chuẩn bị
        VaultEntitlement entitlement = NewEntitlement(ServiceTier.Xs, EntitlementStatus.PaidActive, Now.AddDays(10));

        // Hành động / Kiểm tra: chọn gói đứng sau gói đang dùng
        Assert.Equal(PaymentOrderKind.Upgrade, BillingRules.DetermineKind(Prices, entitlement, "XS_MAX", false, Now));
    }

    [Theory]
    [InlineData("XS")]
    [InlineData("")]
    [InlineData(null)]
    public void Sec83_DetermineKind_PaidActiveNotExpired_NothingToBuy(string? tier)
    {
        // Chuẩn bị
        VaultEntitlement entitlement = NewEntitlement(ServiceTier.Xs, EntitlementStatus.PaidActive, Now.AddDays(10));

        // Hành động
        AppException exception = Assert.Throws<AppException>(
            () => BillingRules.DetermineKind(Prices, entitlement, tier, false, Now));

        // Kiểm tra: còn hạn, không gia hạn, không nâng hạng thì không có gì để mua
        AssertPaymentNotAllowed(exception, "NOTHING_TO_BUY");
    }

    [Theory]
    [InlineData(EntitlementStatus.PaidActive, -1)]
    [InlineData(EntitlementStatus.GraceReadOnly, 30)]
    [InlineData(EntitlementStatus.ExpiredReview, -300)]
    public void Sec83_DetermineKind_NeedsRenewal_IsRenewal(EntitlementStatus status, int daysUntilExpiry)
    {
        // Chuẩn bị: hết hạn, ân hạn hoặc chờ rà soát đều thuộc RENEWAL
        VaultEntitlement entitlement = NewEntitlement(ServiceTier.Xs, status, Now.AddDays(daysUntilExpiry));

        // Hành động / Kiểm tra: chỉ còn đường gia hạn
        Assert.Equal(PaymentOrderKind.Renewal, BillingRules.DetermineKind(Prices, entitlement, "XS", false, Now));
    }

    [Fact]
    public void Sec83_PaidActiveWithoutExpiry_IsRenewal()
    {
        // Chuẩn bị: dữ liệu bất thường, không có hạn thì chỉ cho gia hạn
        VaultEntitlement entitlement = NewEntitlement(ServiceTier.Xs, EntitlementStatus.PaidActive, null);

        // Hành động / Kiểm tra
        Assert.Equal(PaymentOrderKind.Renewal, BillingRules.DetermineKind(Prices, entitlement, "XS", false, Now));
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
    public void Sec83_IsSameSelection_ComparesKindTierAndYears()
    {
        // Chuẩn bị
        PaymentOrder order = NewOrder(PaymentOrderStatus.Pending, Now.AddMinutes(15));
        BillingQuote same = BillingRules.QuotePurchase(Prices, "XS", 3);
        BillingQuote otherTier = BillingRules.QuotePurchase(Prices, "XS_MAX", 3);
        BillingQuote otherYears = BillingRules.QuotePurchase(Prices, "XS", 1);
        VaultEntitlement entitlement = NewEntitlement(ServiceTier.Xs, EntitlementStatus.PaidActive, Now.AddDays(100));
        BillingQuote renewal = BillingRules.QuoteRenewal(Prices, entitlement, "XS", 3, Now);

        // Hành động / Kiểm tra
        Assert.True(BillingRules.IsSameSelection(order, same));
        Assert.False(BillingRules.IsSameSelection(order, otherTier));
        Assert.False(BillingRules.IsSameSelection(order, otherYears));
        Assert.False(BillingRules.IsSameSelection(order, renewal));
    }

    private static PaymentOrder NewPurchaseOrder(string tierText, int years)
    {
        BillingQuote quote = BillingRules.QuotePurchase(Prices, tierText, years);
        PaymentOrder order = new PaymentOrder();
        order.Kind = quote.Kind;
        order.Tier = quote.Tier;
        order.Years = quote.Years;
        order.StorageQuotaBytes = quote.StorageQuotaBytes;
        order.IncludedBeneficiarySlots = quote.IncludedBeneficiarySlots;
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
        // Chuẩn bị: kho Free, đơn XS 3 năm
        VaultEntitlement entitlement = NewEntitlement(ServiceTier.Free, EntitlementStatus.Free, null);
        PaymentOrder order = NewPurchaseOrder("XS", 3);

        // Hành động
        BillingRules.ApplyPurchase(entitlement, order, Now);

        // Kiểm tra: kỳ = 3 × 365 ngày kể từ lúc trả tiền
        Assert.Equal(ServiceTier.Xs, entitlement.Tier);
        Assert.Equal(EntitlementStatus.PaidActive, entitlement.Status);
        Assert.Equal(200 * 1048576L, entitlement.StorageQuotaBytes);
        Assert.Equal(2, entitlement.IncludedBeneficiarySlots);
        Assert.Equal(Now.AddDays(1095), entitlement.ServiceExpiresAt);
        Assert.Equal(Now, entitlement.UpdatedAt);
    }

    [Fact]
    public void Ac94_ApplyPurchase_XsMaxFiveYears_Is500MiBFiveSlots1825Days()
    {
        // Chuẩn bị
        VaultEntitlement entitlement = NewEntitlement(ServiceTier.Free, EntitlementStatus.Free, null);
        PaymentOrder order = NewPurchaseOrder("XS_MAX", 5);

        // Hành động
        BillingRules.ApplyPurchase(entitlement, order, Now);

        // Kiểm tra
        Assert.Equal(ServiceTier.XsMax, entitlement.Tier);
        Assert.Equal(500 * 1048576L, entitlement.StorageQuotaBytes);
        Assert.Equal(5, entitlement.IncludedBeneficiarySlots);
        Assert.Equal(Now.AddDays(1825), entitlement.ServiceExpiresAt);
    }

    [Theory]
    [InlineData(EntitlementStatus.PaidActive)]
    [InlineData(EntitlementStatus.GraceReadOnly)]
    [InlineData(EntitlementStatus.ExpiredReview)]
    public void D8_ApplyPurchase_EntitlementNoLongerFree_NotPayable(EntitlementStatus status)
    {
        // Chuẩn bị: quyền dịch vụ đã đổi từ lúc tạo đơn
        VaultEntitlement entitlement = NewEntitlement(ServiceTier.Xs, status, Now.AddDays(10));
        PaymentOrder order = NewPurchaseOrder("XS_MAX", 1);

        // Hành động
        AppException exception = Assert.Throws<AppException>(() => BillingRules.ApplyPurchase(entitlement, order, Now));

        // Kiểm tra: không ghi gì vào quyền dịch vụ
        AssertNotPayable(exception, "ENTITLEMENT_CHANGED");
        Assert.Equal(ServiceTier.Xs, entitlement.Tier);
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
    [InlineData(PlanStatus.HandoverRequested)]
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
