using LegacyVault.Application.Exceptions;
using LegacyVault.Application.Services;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;
using Xunit;

namespace LegacyVault.IntegrationTests.Payment;

/// <summary>
/// Luật gia hạn (RENEWAL): còn hạn thì nối tiếp hạn cũ, hết hạn hoặc ân hạn thì tính từ lúc thanh toán; giá bằng giá mua mới
/// của hạng được chọn; không gia hạn xuống mức nhỏ hơn dữ liệu đang dùng. Thuần logic, không cần database.
/// </summary>
public sealed class BillingRenewalRulesTests
{
    private static readonly IBillingPriceSettings Prices = new FakeBillingPriceSettings();

    private static readonly DateTimeOffset Now = new DateTimeOffset(2026, 10, 18, 12, 0, 0, TimeSpan.Zero);

    private const long MiB = 1048576L;

    private static VaultEntitlement NewEntitlement(
        ServiceTier tier, EntitlementStatus status, DateTimeOffset? expiresAt, int extraSlots)
    {
        VaultEntitlement entitlement = new VaultEntitlement();
        entitlement.Tier = tier;
        entitlement.Status = status;
        entitlement.ServiceExpiresAt = expiresAt;
        entitlement.ExtraBeneficiarySlots = extraSlots;
        entitlement.StorageQuotaBytes = 200 * MiB;
        entitlement.IncludedBeneficiarySlots = 2;
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
    public void DetermineKind_PaidActiveWithTerm_RenewFlagMakesRenewal()
    {
        // Chuẩn bị
        VaultEntitlement entitlement = NewEntitlement(ServiceTier.Xs, EntitlementStatus.PaidActive, Now.AddDays(100), 0);

        // Hành động / Kiểm tra: có cờ thì gia hạn sớm, không cờ thì vẫn là mua thêm suất như trước
        Assert.Equal(PaymentOrderKind.Renewal, BillingRules.DetermineKind(entitlement, "XS", 0, true, Now));
        Assert.Equal(PaymentOrderKind.Addon, BillingRules.DetermineKind(entitlement, "XS", 1, false, Now));
    }

    [Fact]
    public void DetermineKind_RenewFlagIsIgnoredForFreeVault()
    {
        // Chuẩn bị
        VaultEntitlement entitlement = new VaultEntitlement();
        entitlement.Tier = ServiceTier.Free;
        entitlement.Status = EntitlementStatus.Free;

        // Hành động / Kiểm tra: kho Free luôn là mua mới
        Assert.Equal(PaymentOrderKind.Purchase, BillingRules.DetermineKind(entitlement, "XS", 0, true, Now));
    }

    [Fact]
    public void QuoteRenewal_SameTierThreeYearsWithOneExtraSlot_Is646000()
    {
        // Chuẩn bị: XS còn hạn 100 ngày, gia hạn 3 năm, giữ 1 suất thêm
        DateTimeOffset expiresAt = Now.AddDays(100);
        VaultEntitlement entitlement = NewEntitlement(ServiceTier.Xs, EntitlementStatus.PaidActive, expiresAt, 1);

        // Hành động
        BillingQuote quote = BillingRules.QuoteRenewal(Prices, entitlement, "XS", 3, 1, Now);

        // Kiểm tra: 499.000 + 49.000 × 1 × 3 năm; nối tiếp hạn cũ
        Assert.Equal(PaymentOrderKind.Renewal, quote.Kind);
        Assert.Equal(ServiceTier.Xs, quote.Tier);
        Assert.Equal(3, quote.Years);
        Assert.Equal(1, quote.ExtraSlots);
        Assert.Equal(499000, quote.TierAmount);
        Assert.Equal(147000, quote.SlotAmount);
        Assert.Equal(646000, quote.TotalAmount);
        Assert.Equal(1095, quote.ServiceDays);
        Assert.Equal(200 * MiB, quote.StorageQuotaBytes);
        Assert.Equal(2, quote.IncludedBeneficiarySlots);
        Assert.Equal(3, quote.TotalBeneficiarySlots);
        Assert.Equal(expiresAt, quote.BaseServiceExpiresAt);
    }

    [Theory]
    [InlineData(EntitlementStatus.GraceReadOnly, -30)]
    [InlineData(EntitlementStatus.ExpiredReview, -300)]
    [InlineData(EntitlementStatus.PaidActive, -1)]
    public void QuoteRenewal_ExpiredGraceOrReview_HasNoBaseSoTermStartsOnPayment(
        EntitlementStatus status, int daysUntilExpiry)
    {
        // Chuẩn bị
        VaultEntitlement entitlement = NewEntitlement(ServiceTier.Xs, status, Now.AddDays(daysUntilExpiry), 0);

        // Hành động
        BillingQuote quote = BillingRules.QuoteRenewal(Prices, entitlement, "XS", 1, 0, Now);

        // Kiểm tra: không có hạn cũ để nối tiếp
        Assert.Null(quote.BaseServiceExpiresAt);
        Assert.Equal(199000, quote.TotalAmount);
    }

    [Fact]
    public void QuoteRenewal_AfterExpiry_CanChooseHigherTier()
    {
        // Chuẩn bị: kho XS đang ân hạn, gia hạn lên XS Max 1 năm (kỳ mới tính từ lúc trả nên chọn hạng tự do)
        VaultEntitlement entitlement = NewEntitlement(ServiceTier.Xs, EntitlementStatus.GraceReadOnly, Now.AddDays(-10), 0);

        // Hành động
        BillingQuote quote = BillingRules.QuoteRenewal(Prices, entitlement, "XS_MAX", 1, 0, Now);

        // Kiểm tra: giá niêm yết XS Max 1 năm, 500 MiB, 5 suất kèm
        Assert.Equal(399000, quote.TotalAmount);
        Assert.Equal(500 * MiB, quote.StorageQuotaBytes);
        Assert.Equal(5, quote.IncludedBeneficiarySlots);
    }

    [Theory]
    [InlineData("XS_MAX")]
    [InlineData("xs_max")]
    public void QuoteRenewal_EarlyRenewalWithDifferentTier_Returns400OnTier(string tier)
    {
        // Chuẩn bị: XS còn hạn; đổi hạng lúc gia hạn sớm sẽ được cấp ngay và cho dùng hạng mới không đúng giá
        VaultEntitlement entitlement = NewEntitlement(ServiceTier.Xs, EntitlementStatus.PaidActive, Now.AddDays(10), 0);

        // Hành động / Kiểm tra
        AppException exception = Assert.Throws<AppException>(() => BillingRules.QuoteRenewal(Prices, entitlement, tier, 1, 0, Now));
        AssertValidationOnField(exception, "tier");
    }

    [Fact]
    public void QuoteRenewal_MissingTier_Returns400OnTier()
    {
        VaultEntitlement entitlement = NewEntitlement(ServiceTier.Xs, EntitlementStatus.GraceReadOnly, Now.AddDays(-5), 0);
        AppException exception = Assert.Throws<AppException>(() => BillingRules.QuoteRenewal(Prices, entitlement, null, 1, 0, Now));
        AssertValidationOnField(exception, "tier");
    }

    [Fact]
    public void QuoteRenewal_MissingOrUnsupportedYears_Returns400OnYears()
    {
        VaultEntitlement entitlement = NewEntitlement(ServiceTier.Xs, EntitlementStatus.GraceReadOnly, Now.AddDays(-5), 0);
        AssertValidationOnField(
            Assert.Throws<AppException>(() => BillingRules.QuoteRenewal(Prices, entitlement, "XS", null, 0, Now)), "years");
        AssertValidationOnField(
            Assert.Throws<AppException>(() => BillingRules.QuoteRenewal(Prices, entitlement, "XS", 2, 0, Now)), "years");
    }

    [Fact]
    public void QuoteRenewal_TooManyExtraSlots_Returns400OnExtraSlots()
    {
        VaultEntitlement entitlement = NewEntitlement(ServiceTier.Xs, EntitlementStatus.GraceReadOnly, Now.AddDays(-5), 0);
        AssertValidationOnField(
            Assert.Throws<AppException>(() => BillingRules.QuoteRenewal(Prices, entitlement, "XS", 1, 21, Now)), "extraSlots");
    }

    [Fact]
    public void EnsureUsageFits_UsageWithinPlan_IsAllowed()
    {
        // Chuẩn bị: XS (200 MiB, 2 suất) đúng bằng mức đang dùng
        BillingQuote quote = BillingRules.QuotePurchase(Prices, "XS", 1, 0);

        // Hành động / Kiểm tra: dùng đúng bằng hạn mức vẫn được
        BillingRules.EnsureUsageFitsRenewal(quote, 200 * MiB, 2);
    }

    [Fact]
    public void EnsureUsageFits_StorageOverQuota_IsRejected()
    {
        BillingQuote quote = BillingRules.QuotePurchase(Prices, "XS", 1, 0);
        AppException exception = Assert.Throws<AppException>(
            () => BillingRules.EnsureUsageFitsRenewal(quote, 200 * MiB + 1, 0));
        AssertPaymentNotAllowed(exception, "USAGE_EXCEEDS_PLAN");
    }

    [Fact]
    public void EnsureUsageFits_MoreBeneficiariesThanSlots_IsRejected()
    {
        // Chuẩn bị: XS có 2 suất, đang dùng 3
        BillingQuote quote = BillingRules.QuotePurchase(Prices, "XS", 1, 0);
        AppException exception = Assert.Throws<AppException>(() => BillingRules.EnsureUsageFitsRenewal(quote, 0, 3));
        AssertPaymentNotAllowed(exception, "USAGE_EXCEEDS_PLAN");
    }

    private static PaymentOrder NewRenewalOrder(
        ServiceTier tier, int years, int extraSlots, DateTimeOffset? baseExpiresAt)
    {
        PaymentOrder order = new PaymentOrder();
        order.Kind = PaymentOrderKind.Renewal;
        order.Tier = tier;
        order.Years = years;
        order.ExtraSlots = extraSlots;
        order.StorageQuotaBytes = BillingPrices.GetStorageQuotaBytes(tier);
        order.IncludedBeneficiarySlots = BillingPrices.GetIncludedSlots(tier);
        order.BaseServiceExpiresAt = baseExpiresAt;
        return order;
    }

    [Fact]
    public void ApplyRenewal_EarlyRenewal_ExtendsFromOldExpiryAndKeepsRemainingDays()
    {
        // Chuẩn bị: còn 100 ngày, gia hạn 3 năm, giữ 1 suất thêm
        DateTimeOffset expiresAt = Now.AddDays(100);
        VaultEntitlement entitlement = NewEntitlement(ServiceTier.Xs, EntitlementStatus.PaidActive, expiresAt, 1);
        PaymentOrder order = NewRenewalOrder(ServiceTier.Xs, 3, 1, expiresAt);

        // Hành động
        BillingRules.ApplyRenewal(entitlement, order, Now);

        // Kiểm tra: không mất ngày còn lại, hạn mới = hạn cũ + 1.095 ngày
        Assert.Equal(expiresAt.AddDays(1095), entitlement.ServiceExpiresAt);
        Assert.Equal(EntitlementStatus.PaidActive, entitlement.Status);
        Assert.Equal(1, entitlement.ExtraBeneficiarySlots);
    }

    [Fact]
    public void ApplyRenewal_LateRenewalAfterGrace_StartsFromPaymentTimeAndRestoresPaidActive()
    {
        // Chuẩn bị: đã ân hạn, hạn cũ cách đây 30 ngày
        VaultEntitlement entitlement = NewEntitlement(
            ServiceTier.Xs, EntitlementStatus.GraceReadOnly, Now.AddDays(-30), 0);
        PaymentOrder order = NewRenewalOrder(ServiceTier.Xs, 1, 0, null);

        // Hành động
        BillingRules.ApplyRenewal(entitlement, order, Now);

        // Kiểm tra: kỳ mới tính từ lúc trả; ngày ân hạn không được cộng bù
        Assert.Equal(Now.AddDays(365), entitlement.ServiceExpiresAt);
        Assert.Equal(EntitlementStatus.PaidActive, entitlement.Status);
    }

    [Fact]
    public void ApplyRenewal_ExpiredReview_CanBeRenewed()
    {
        // Chuẩn bị: hết ân hạn, chờ rà soát
        VaultEntitlement entitlement = NewEntitlement(
            ServiceTier.Xs, EntitlementStatus.ExpiredReview, Now.AddDays(-300), 0);
        PaymentOrder order = NewRenewalOrder(ServiceTier.Xs, 1, 0, null);

        // Hành động
        BillingRules.ApplyRenewal(entitlement, order, Now);

        // Kiểm tra
        Assert.Equal(EntitlementStatus.PaidActive, entitlement.Status);
        Assert.Equal(Now.AddDays(365), entitlement.ServiceExpiresAt);
    }

    [Fact]
    public void ApplyRenewal_AfterExpiry_ChangesTierQuotaAndSlotsToTheOrder()
    {
        // Chuẩn bị: kho XS đang ân hạn có 2 suất thêm, gia hạn lên XS Max không giữ suất thêm
        VaultEntitlement entitlement = NewEntitlement(ServiceTier.Xs, EntitlementStatus.GraceReadOnly, Now.AddDays(-10), 2);
        PaymentOrder order = NewRenewalOrder(ServiceTier.XsMax, 1, 0, null);

        // Hành động
        BillingRules.ApplyRenewal(entitlement, order, Now);

        // Kiểm tra
        Assert.Equal(ServiceTier.XsMax, entitlement.Tier);
        Assert.Equal(500 * MiB, entitlement.StorageQuotaBytes);
        Assert.Equal(5, entitlement.IncludedBeneficiarySlots);
        Assert.Equal(0, entitlement.ExtraBeneficiarySlots);
    }

    [Fact]
    public void ApplyRenewal_EarlyRenewalButTierChangedSinceOrder_IsRejected()
    {
        // Chuẩn bị: đơn gia hạn sớm XS nhưng kho đã lên XS Max bằng đơn nâng hạng trong lúc chờ trả
        DateTimeOffset expiresAt = Now.AddDays(100);
        VaultEntitlement entitlement = NewEntitlement(ServiceTier.XsMax, EntitlementStatus.PaidActive, expiresAt, 0);
        PaymentOrder order = NewRenewalOrder(ServiceTier.Xs, 1, 0, expiresAt);

        // Hành động / Kiểm tra: không hạ hạng của kho
        Assert.Throws<AppException>(() => BillingRules.ApplyRenewal(entitlement, order, Now));
        Assert.Equal(ServiceTier.XsMax, entitlement.Tier);
    }

    [Fact]
    public void ApplyRenewal_EarlyRenewalPaidAfterOldExpiry_StartsFromPaymentTime()
    {
        // Chuẩn bị: đơn tạo lúc còn hạn nhưng tiền tới sau hạn cũ (chưa có job đổi trạng thái)
        DateTimeOffset expiresAt = Now.AddMinutes(5);
        VaultEntitlement entitlement = NewEntitlement(ServiceTier.Xs, EntitlementStatus.PaidActive, expiresAt, 0);
        PaymentOrder order = NewRenewalOrder(ServiceTier.Xs, 1, 0, expiresAt);
        DateTimeOffset paidAt = Now.AddMinutes(10);

        // Hành động
        BillingRules.ApplyRenewal(entitlement, order, paidAt);

        // Kiểm tra: không lùi về hạn cũ đã qua
        Assert.Equal(paidAt.AddDays(365), entitlement.ServiceExpiresAt);
    }

    [Fact]
    public void ApplyRenewal_EarlyRenewalButExpiryChangedSinceOrder_IsRejected()
    {
        // Chuẩn bị: hạn của kho khác hạn lúc tạo đơn
        VaultEntitlement entitlement = NewEntitlement(ServiceTier.Xs, EntitlementStatus.PaidActive, Now.AddDays(50), 0);
        PaymentOrder order = NewRenewalOrder(ServiceTier.Xs, 1, 0, Now.AddDays(100));

        // Hành động / Kiểm tra
        Assert.Throws<AppException>(() => BillingRules.ApplyRenewal(entitlement, order, Now));
        Assert.Equal(Now.AddDays(50), entitlement.ServiceExpiresAt);
    }

    [Fact]
    public void ApplyRenewal_LateRenewalButVaultNowHasActiveTerm_IsRejected()
    {
        // Chuẩn bị: đơn tạo lúc hết hạn nhưng giữa chừng kho đã có kỳ còn hạn khác (không cấp chồng)
        VaultEntitlement entitlement = NewEntitlement(ServiceTier.Xs, EntitlementStatus.PaidActive, Now.AddDays(200), 0);
        PaymentOrder order = NewRenewalOrder(ServiceTier.Xs, 1, 0, null);

        // Hành động / Kiểm tra
        Assert.Throws<AppException>(() => BillingRules.ApplyRenewal(entitlement, order, Now));
    }

    [Fact]
    public void ApplyRenewal_FreeEntitlement_IsRejected()
    {
        // Chuẩn bị
        VaultEntitlement entitlement = new VaultEntitlement();
        entitlement.Tier = ServiceTier.Free;
        entitlement.Status = EntitlementStatus.Free;
        PaymentOrder order = NewRenewalOrder(ServiceTier.Xs, 1, 0, null);

        // Hành động / Kiểm tra
        Assert.Throws<AppException>(() => BillingRules.ApplyRenewal(entitlement, order, Now));
    }

    [Fact]
    public void OrderExpiry_Renewal_IsFifteenMinutesEvenNearEndOfTerm()
    {
        // Hành động: gia hạn không bị chặn bởi hết kỳ (khác mua thêm suất)
        DateTimeOffset expiresAt = BillingRules.ComputeOrderExpiresAt(PaymentOrderKind.Renewal, Now, Now.AddMinutes(5));

        // Kiểm tra
        Assert.Equal(Now.AddMinutes(15), expiresAt);
    }
}
