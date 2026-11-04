using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;
using LegacyVault.IntegrationTests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace LegacyVault.IntegrationTests.Payment;

/// <summary>GET /api/v1/billing/plans và POST /api/v1/billing/quote trên API thật và SQL Server tạm (SRS §8.1–8.3).</summary>
public sealed class BillingQuoteFlowTests : IAsyncLifetime
{
    private ApiFactory factory = null!;

    public async Task InitializeAsync()
    {
        ApiFactory? created = ApiFactory.CreateForTempDatabase();
        if (created == null)
        {
            return;
        }
        factory = created;
        await factory.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        if (factory == null)
        {
            return;
        }
        await factory.DropAsync();
        await factory.DisposeAsync();
    }

    /// <summary>User mới có kho Free, kèm eKYC Owner đạt cách đây ekycDaysAgo ngày (null = chưa có eKYC).</summary>
    private async Task<(HttpClient Client, Guid VaultId, Guid OwnerId)> CreateFreeVaultAsync(int? ekycDaysAgo)
    {
        HttpClient client = await ApiTestHelpers.CreateClientForNewUserAsync(factory, emailVerified: true);
        Guid vaultId = await ApiTestHelpers.CreateVaultAsync(client);
        Guid ownerId = Guid.Empty;
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            Vault vault = await db.Vaults.SingleAsync(v => v.Id == vaultId);
            ownerId = vault.OwnerId;
            if (ekycDaysAgo != null)
            {
                EkycVerification verification = new EkycVerification();
                verification.UserId = ownerId;
                verification.Purpose = EkycPurpose.OwnerEnrollment;
                verification.Status = EkycStatus.Passed;
                verification.CreatedAt = DateTimeOffset.UtcNow.AddDays(-ekycDaysAgo.Value);
                verification.CompletedAt = DateTimeOffset.UtcNow.AddDays(-ekycDaysAgo.Value);
                db.EkycVerifications.Add(verification);
                await db.SaveChangesAsync();
            }
        });
        return (client, vaultId, ownerId);
    }

    /// <summary>Đặt kho thành XS đang trả phí, hết hạn sau daysLeft ngày (sửa thẳng DB, chưa có dev-pay).</summary>
    private async Task MakePaidActiveAsync(Guid vaultId, int daysLeft)
    {
        await ApiTestHelpers.UpgradeVaultToXsAsync(factory, vaultId);
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            DateTimeOffset expiresAt = DateTimeOffset.UtcNow.AddDays(daysLeft);
            await db.VaultEntitlements.Where(e => e.VaultId == vaultId)
                .ExecuteUpdateAsync(s => s.SetProperty(e => e.ServiceExpiresAt, expiresAt));
        });
    }

    private static Task<HttpResponseMessage> QuoteAsync(HttpClient client, object body)
    {
        return client.PostAsJsonAsync("/api/v1/billing/quote", body);
    }

    private async Task<int> CountPaymentOrdersAsync()
    {
        int count = 0;
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            count = await db.PaymentOrders.CountAsync();
        });
        return count;
    }

    [SqlFact]
    public async Task Sec81_GetPlans_ReturnsPriceTable()
    {
        // Chuẩn bị
        HttpClient client = await ApiTestHelpers.CreateClientForNewUserAsync(factory, emailVerified: true);

        // Hành động
        HttpResponseMessage response = await client.GetAsync("/api/v1/billing/plans");

        // Kiểm tra
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        JsonElement plans = await ApiTestHelpers.ReadJsonAsync(response);
        Assert.Equal("2026-10", plans.GetProperty("policyVersion").GetString());
        Assert.Equal("VND", plans.GetProperty("currency").GetString());
        JsonElement tiers = plans.GetProperty("tiers");
        Assert.Equal(2, tiers.GetArrayLength());
        Assert.Equal("XS", tiers[0].GetProperty("tier").GetString());
        Assert.Equal(2, tiers[0].GetProperty("includedBeneficiarySlots").GetInt32());
        Assert.Equal(200 * 1048576L, tiers[0].GetProperty("storageQuotaBytes").GetInt64());
        Assert.Equal(199000, tiers[0].GetProperty("prices")[0].GetProperty("amount").GetInt64());
        Assert.Equal("XS_MAX", tiers[1].GetProperty("tier").GetString());
        Assert.Equal(1599000, tiers[1].GetProperty("prices")[2].GetProperty("amount").GetInt64());
        Assert.Equal(1825, tiers[1].GetProperty("prices")[2].GetProperty("serviceDays").GetInt32());
        Assert.Equal(49000, plans.GetProperty("extraSlot").GetProperty("pricePerYear").GetInt64());
        Assert.Equal(20, plans.GetProperty("extraSlot").GetProperty("maxPerOrder").GetInt32());
    }

    [SqlFact]
    public async Task Sec81_Plans_WithoutLogin_ReturnsPublicPriceList_ButQuoteStillReturns401()
    {
        // Chuẩn bị
        HttpClient anonymous = factory.CreateClient();

        // Hành động
        HttpResponseMessage plans = await anonymous.GetAsync("/api/v1/billing/plans");
        HttpResponseMessage quote = await QuoteAsync(anonymous, new { tier = "XS", years = 1, extraSlots = 0 });

        // Kiểm tra: bảng giá là thông tin công khai cho trang giới thiệu; báo giá và đơn vẫn cần đăng nhập
        Assert.Equal(HttpStatusCode.OK, plans.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, quote.StatusCode);
    }

    [SqlFact]
    public async Task Ac94_Quote_PurchaseXsThreeYearsOneSlot_Is646000_AndWritesNothing()
    {
        // Chuẩn bị
        (HttpClient client, Guid vaultId, Guid ownerId) = await CreateFreeVaultAsync(ekycDaysAgo: 1);

        // Hành động: body cố ý kèm số tiền và loại đơn giả, backend phải bỏ qua (PAY-11)
        HttpResponseMessage response = await QuoteAsync(client,
            new { tier = "XS", years = 3, extraSlots = 1, totalAmount = 1, kind = "ADDON", status = "PAID" });

        // Kiểm tra
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        JsonElement quote = await ApiTestHelpers.ReadJsonAsync(response);
        Assert.Equal("PURCHASE", quote.GetProperty("kind").GetString());
        Assert.Equal("XS", quote.GetProperty("tier").GetString());
        Assert.Equal(3, quote.GetProperty("years").GetInt32());
        Assert.Equal(499000, quote.GetProperty("tierAmount").GetInt64());
        Assert.Equal(147000, quote.GetProperty("slotAmount").GetInt64());
        Assert.Equal(646000, quote.GetProperty("totalAmount").GetInt64());
        Assert.Equal("VND", quote.GetProperty("currency").GetString());
        Assert.Equal(3, quote.GetProperty("beneficiarySlots").GetInt32());
        Assert.Equal(1095, quote.GetProperty("serviceDays").GetInt32());
        Assert.Equal(200 * 1048576L, quote.GetProperty("storageQuotaBytes").GetInt64());
        Assert.Equal("ON_PAYMENT", quote.GetProperty("serviceStartsAt").GetString());
        Assert.Equal("2026-10", quote.GetProperty("policyVersion").GetString());
        Assert.Equal("Thanh toán không tự kích hoạt bàn giao.", quote.GetProperty("notice").GetString());
        Assert.Equal(0, await CountPaymentOrdersAsync());
    }

    [SqlFact]
    public async Task Ac94_Quote_PurchaseXsMaxFiveYears_Is1599000()
    {
        // Chuẩn bị
        (HttpClient client, _, _) = await CreateFreeVaultAsync(ekycDaysAgo: 1);

        // Hành động
        HttpResponseMessage response = await QuoteAsync(client, new { tier = "XS_MAX", years = 5, extraSlots = 0 });

        // Kiểm tra
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        JsonElement quote = await ApiTestHelpers.ReadJsonAsync(response);
        Assert.Equal(1599000, quote.GetProperty("totalAmount").GetInt64());
        Assert.Equal(5, quote.GetProperty("beneficiarySlots").GetInt32());
        Assert.Equal(1825, quote.GetProperty("serviceDays").GetInt32());
    }

    /// <summary>Admin tắt mở bán một hạng: ghi cờ Plans.{Xs|XsMax}.On = false thẳng vào DB.</summary>
    private async Task SetTierOnSaleOffAsync(string key)
    {
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            await db.SystemSettings.Where(s => s.Key == key).ExecuteDeleteAsync();
            SystemSetting setting = new SystemSetting();
            setting.Key = key;
            setting.Value = "false";
            setting.DataType = "BOOLEAN";
            setting.Category = "Plans";
            setting.UpdatedBy = "admin@test";
            db.SystemSettings.Add(setting);
            await db.SaveChangesAsync();
        });
    }

    /// <summary>Admin lưu một tham số Plans.* (UpdatedBy khác null nên đè lên cấu hình gốc).</summary>
    private async Task SetAdminSettingAsync(string key, string value, string dataType)
    {
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            await db.SystemSettings.Where(s => s.Key == key).ExecuteDeleteAsync();
            SystemSetting setting = new SystemSetting();
            setting.Key = key;
            setting.Value = value;
            setting.DataType = dataType;
            setting.Category = "Plans";
            setting.UpdatedBy = "admin@test";
            db.SystemSettings.Add(setting);
            await db.SaveChangesAsync();
        });
    }

    [SqlFact]
    public async Task Sec81_GetPlans_AdminAddsSevenYearPeriodAndStopsFive_ListsOnlySaleYears()
    {
        // Chuẩn bị: thêm kỳ 7 năm (kèm giá hai hạng), tạm ngừng kỳ 5 năm
        await SetAdminSettingAsync("Plans.Xs.Price7Y", "1099000", "NUMBER");
        await SetAdminSettingAsync("Plans.XsMax.Price7Y", "2199000", "NUMBER");
        await SetAdminSettingAsync("Plans.Periods",
            "[{\"years\":1,\"on\":true},{\"years\":3,\"on\":true},{\"years\":5,\"on\":false},{\"years\":7,\"on\":true}]", "JSON");
        (HttpClient client, _, _) = await CreateFreeVaultAsync(ekycDaysAgo: 1);

        // Hành động
        HttpResponseMessage plans = await client.GetAsync("/api/v1/billing/plans");
        HttpResponseMessage sevenYears = await QuoteAsync(client, new { tier = "XS", years = 7, extraSlots = 0 });
        HttpResponseMessage fiveYears = await QuoteAsync(client, new { tier = "XS", years = 5, extraSlots = 0 });

        // Kiểm tra: bảng giá chỉ có 1, 3, 7 năm; báo giá 7 năm đúng tiền; kỳ 5 năm bị từ chối
        JsonElement prices = (await ApiTestHelpers.ReadJsonAsync(plans)).GetProperty("tiers")[0].GetProperty("prices");
        List<int> years = new List<int>();
        foreach (JsonElement price in prices.EnumerateArray())
        {
            years.Add(price.GetProperty("years").GetInt32());
        }
        Assert.Equal(new List<int> { 1, 3, 7 }, years);
        Assert.Equal(HttpStatusCode.OK, sevenYears.StatusCode);
        Assert.Equal(1099000, (await ApiTestHelpers.ReadJsonAsync(sevenYears)).GetProperty("tierAmount").GetInt64());
        Assert.Equal(HttpStatusCode.BadRequest, fiveYears.StatusCode);
    }

    [SqlFact]
    public async Task Sec81_GetPlansAndQuote_AdminChangesQuotaAndSlots_UsesNewLimits()
    {
        // Chuẩn bị: Admin đổi dung lượng XS Max lên 800 MiB và suất người nhận kèm lên 9
        await SetAdminSettingAsync("Plans.XsMax.QuotaMb", "800", "NUMBER");
        await SetAdminSettingAsync("Plans.XsMax.Slots", "9", "NUMBER");
        (HttpClient client, _, _) = await CreateFreeVaultAsync(ekycDaysAgo: 1);

        // Hành động
        HttpResponseMessage plans = await client.GetAsync("/api/v1/billing/plans");
        HttpResponseMessage quote = await QuoteAsync(client, new { tier = "XS_MAX", years = 1, extraSlots = 0 });

        // Kiểm tra: bảng giá và báo giá lấy hạn mức mới; hạng XS không bị ảnh hưởng
        JsonElement tiers = (await ApiTestHelpers.ReadJsonAsync(plans)).GetProperty("tiers");
        Assert.Equal(200 * 1048576L, tiers[0].GetProperty("storageQuotaBytes").GetInt64());
        Assert.Equal(800 * 1048576L, tiers[1].GetProperty("storageQuotaBytes").GetInt64());
        Assert.Equal(9, tiers[1].GetProperty("includedBeneficiarySlots").GetInt32());
        JsonElement quoted = await ApiTestHelpers.ReadJsonAsync(quote);
        Assert.Equal(800 * 1048576L, quoted.GetProperty("storageQuotaBytes").GetInt64());
        Assert.Equal(9, quoted.GetProperty("includedBeneficiarySlots").GetInt32());
    }

    [SqlFact]
    public async Task Sec81_GetPlans_AdminHidesXsAndEditsXsMax_ReturnsEditedXsMax()
    {
        // Chuẩn bị: đúng trạng thái admin đã lưu trên máy phát triển
        await SetAdminSettingAsync("Plans.Xs.Hidden", "true", "BOOLEAN");
        await SetAdminSettingAsync("Plans.Xs.Price1Y", "10000", "NUMBER");
        await SetAdminSettingAsync("Plans.XsMax.Price1Y", "50000", "NUMBER");
        await SetAdminSettingAsync("Plans.XsMax.QuotaMb", "45234", "NUMBER");
        await SetAdminSettingAsync("Plans.XsMax.Slots", "90", "NUMBER");
        HttpClient client = await ApiTestHelpers.CreateClientForNewUserAsync(factory, emailVerified: true);

        // Hành động
        HttpResponseMessage response = await client.GetAsync("/api/v1/billing/plans");

        // Kiểm tra
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        JsonElement plans = await ApiTestHelpers.ReadJsonAsync(response);
        JsonElement tiers = plans.GetProperty("tiers");
        Assert.Equal(2, tiers.GetArrayLength());
        Assert.True(tiers[0].GetProperty("hidden").GetBoolean());
        Assert.Equal("XS_MAX", tiers[1].GetProperty("tier").GetString());
        Assert.Equal(45234 * 1048576L, tiers[1].GetProperty("storageQuotaBytes").GetInt64());
        Assert.Equal(90, tiers[1].GetProperty("includedBeneficiarySlots").GetInt32());
        Assert.Equal(50000, tiers[1].GetProperty("prices")[0].GetProperty("amount").GetInt64());
        Assert.StartsWith("DB-", plans.GetProperty("policyVersion").GetString());
    }

    /// <summary>Admin tạo gói GOLD (đứng sau XS Max) với giá ba kỳ, dung lượng 1000 MiB, 7 suất, đang mở bán.</summary>
    private async Task CreateGoldPlanAsync(bool onSale)
    {
        await SetAdminSettingAsync("Plans.Catalog", "[{\"code\":\"GOLD\",\"name\":\"Gold\",\"subtitle\":\"Cao nhất\",\"order\":30}]", "JSON");
        await SetAdminSettingAsync("Plans.GOLD.On", onSale ? "true" : "false", "BOOLEAN");
        await SetAdminSettingAsync("Plans.GOLD.QuotaMb", "1000", "NUMBER");
        await SetAdminSettingAsync("Plans.GOLD.Slots", "7", "NUMBER");
        await SetAdminSettingAsync("Plans.GOLD.Price1Y", "599000", "NUMBER");
        await SetAdminSettingAsync("Plans.GOLD.Price3Y", "1499000", "NUMBER");
        await SetAdminSettingAsync("Plans.GOLD.Price5Y", "2399000", "NUMBER");
    }

    [SqlFact]
    public async Task Sec81_AdminCreatesNewPlan_ListedPurchasableAndUpgradeTarget()
    {
        // Chuẩn bị: gói GOLD do Admin tạo; kho đang dùng XS còn 180 ngày
        await CreateGoldPlanAsync(onSale: true);
        (HttpClient client, Guid vaultId, _) = await CreateFreeVaultAsync(ekycDaysAgo: 1);

        // Hành động 1: bảng giá và báo giá mua mới gói GOLD (kho còn Free)
        HttpResponseMessage plans = await client.GetAsync("/api/v1/billing/plans");
        HttpResponseMessage purchase = await QuoteAsync(client, new { tier = "GOLD", years = 3, extraSlots = 0 });

        // Kiểm tra 1: GOLD đứng cuối bảng giá với tên và giá do Admin đặt; báo giá đúng tiền, dung lượng, suất
        JsonElement tiers = (await ApiTestHelpers.ReadJsonAsync(plans)).GetProperty("tiers");
        Assert.Equal(3, tiers.GetArrayLength());
        Assert.Equal("GOLD", tiers[2].GetProperty("tier").GetString());
        Assert.Equal("Gold", tiers[2].GetProperty("name").GetString());
        Assert.Equal(1000 * 1048576L, tiers[2].GetProperty("storageQuotaBytes").GetInt64());
        Assert.True(tiers[2].GetProperty("onSale").GetBoolean());
        JsonElement quoted = await ApiTestHelpers.ReadJsonAsync(purchase);
        Assert.Equal("PURCHASE", quoted.GetProperty("kind").GetString());
        Assert.Equal("GOLD", quoted.GetProperty("tier").GetString());
        Assert.Equal(1499000, quoted.GetProperty("tierAmount").GetInt64());
        Assert.Equal(7, quoted.GetProperty("includedBeneficiarySlots").GetInt32());

        // Hành động 2: kho thành XS còn hạn rồi báo giá nâng lên GOLD
        await MakePaidActiveAsync(vaultId, daysLeft: 180);
        HttpResponseMessage upgrade = await QuoteAsync(client, new { tier = "GOLD", extraSlots = 0 });

        // Kiểm tra 2: là nâng hạng lên GOLD, hạn mức theo GOLD, giá là phần chênh (nhỏ hơn chênh cả kỳ)
        JsonElement upgraded = await ApiTestHelpers.ReadJsonAsync(upgrade);
        Assert.Equal("UPGRADE", upgraded.GetProperty("kind").GetString());
        Assert.Equal("GOLD", upgraded.GetProperty("tier").GetString());
        Assert.Equal(7, upgraded.GetProperty("includedBeneficiarySlots").GetInt32());
        long difference = 599000 - 199000;
        Assert.InRange(upgraded.GetProperty("tierAmount").GetInt64(), 1, difference);
    }

    [SqlFact]
    public async Task Sec83_AdminNewPlanTurnedOff_PurchaseBlockedAsTierNotOnSale()
    {
        // Chuẩn bị: gói GOLD tồn tại nhưng đang tạm ngừng
        await CreateGoldPlanAsync(onSale: false);
        (HttpClient client, _, _) = await CreateFreeVaultAsync(ekycDaysAgo: 1);

        // Hành động
        HttpResponseMessage quote = await QuoteAsync(client, new { tier = "GOLD", years = 1, extraSlots = 0 });

        // Kiểm tra
        JsonElement problem = await ApiTestHelpers.AssertProblemAsync(quote, HttpStatusCode.Conflict, "PAYMENT_NOT_ALLOWED");
        Assert.Equal("TIER_NOT_ON_SALE", problem.GetProperty("reason").GetString());
    }

    [SqlFact]
    public async Task Sec83_Quote_UnknownPlanCode_Returns400()
    {
        // Chuẩn bị
        (HttpClient client, _, _) = await CreateFreeVaultAsync(ekycDaysAgo: 1);

        // Hành động
        HttpResponseMessage quote = await QuoteAsync(client, new { tier = "PLATINUM", years = 1, extraSlots = 0 });

        // Kiểm tra: mã gói không tồn tại là lỗi dữ liệu, không phải 500
        Assert.Equal(HttpStatusCode.BadRequest, quote.StatusCode);
    }

    [SqlFact]
    public async Task Sec81_GetPlans_AdminWritesFeatureLines_ReturnsTrimmedLinesWithoutBlanks()
    {
        // Chuẩn bị: Admin viết mô tả tính năng cho XS Max và Free (có dòng trống và khoảng trắng thừa)
        await SetAdminSettingAsync("Plans.XsMax.Features", "[\"  Lưu 500 MiB  \",\"\",\"Hỗ trợ ưu tiên\"]", "JSON");
        await SetAdminSettingAsync("Plans.Free.Features", "[\"Dùng thử miễn phí\"]", "JSON");
        HttpClient client = await ApiTestHelpers.CreateClientForNewUserAsync(factory, emailVerified: true);

        // Hành động
        HttpResponseMessage response = await client.GetAsync("/api/v1/billing/plans");

        // Kiểm tra: dòng được cắt khoảng trắng, bỏ dòng trống; gói chưa viết thì rỗng để Client dùng mô tả mặc định
        JsonElement plans = await ApiTestHelpers.ReadJsonAsync(response);
        JsonElement xsMax = plans.GetProperty("tiers")[1].GetProperty("features");
        Assert.Equal(2, xsMax.GetArrayLength());
        Assert.Equal("Lưu 500 MiB", xsMax[0].GetString());
        Assert.Equal("Hỗ trợ ưu tiên", xsMax[1].GetString());
        Assert.Equal(0, plans.GetProperty("tiers")[0].GetProperty("features").GetArrayLength());
        Assert.Equal("Dùng thử miễn phí", plans.GetProperty("freeFeatures")[0].GetString());
    }

    [SqlFact]
    public async Task Sec83_Quote_AdminHidesTier_TierNotOnSale()
    {
        // Chuẩn bị: Admin xóa (ẩn) hạng XS
        await SetAdminSettingAsync("Plans.Xs.Hidden", "true", "BOOLEAN");
        (HttpClient client, _, _) = await CreateFreeVaultAsync(ekycDaysAgo: 1);

        // Hành động
        HttpResponseMessage plans = await client.GetAsync("/api/v1/billing/plans");
        HttpResponseMessage quote = await QuoteAsync(client, new { tier = "XS", years = 1, extraSlots = 0 });

        // Kiểm tra: hạng đã xóa vẫn có trong danh sách nhưng kèm hidden và không mua được
        JsonElement tiers = (await ApiTestHelpers.ReadJsonAsync(plans)).GetProperty("tiers");
        Assert.Equal(2, tiers.GetArrayLength());
        Assert.True(tiers[0].GetProperty("hidden").GetBoolean());
        Assert.False(tiers[0].GetProperty("onSale").GetBoolean());
        JsonElement problem = await ApiTestHelpers.AssertProblemAsync(quote, HttpStatusCode.Conflict, "PAYMENT_NOT_ALLOWED");
        Assert.Equal("TIER_NOT_ON_SALE", problem.GetProperty("reason").GetString());
    }

    [SqlFact]
    public async Task Sec81_GetPlans_TierTurnedOffByAdmin_ReportsOnSaleFalse()
    {
        // Chuẩn bị
        await SetTierOnSaleOffAsync("Plans.Xs.On");
        HttpClient client = await ApiTestHelpers.CreateClientForNewUserAsync(factory, emailVerified: true);

        // Hành động
        HttpResponseMessage response = await client.GetAsync("/api/v1/billing/plans");

        // Kiểm tra: chỉ XS bị tắt, XS_MAX vẫn bán
        JsonElement tiers = (await ApiTestHelpers.ReadJsonAsync(response)).GetProperty("tiers");
        Assert.False(tiers[0].GetProperty("onSale").GetBoolean());
        Assert.True(tiers[1].GetProperty("onSale").GetBoolean());
    }

    [SqlFact]
    public async Task Sec83_Quote_And_CreateOrder_TierTurnedOffByAdmin_TierNotOnSale()
    {
        // Chuẩn bị
        await SetTierOnSaleOffAsync("Plans.Xs.On");
        (HttpClient client, _, _) = await CreateFreeVaultAsync(ekycDaysAgo: 1);

        // Hành động
        HttpResponseMessage quote = await QuoteAsync(client, new { tier = "XS", years = 1, extraSlots = 0 });
        HttpResponseMessage order = await client.PostAsJsonAsync("/api/v1/billing/orders", new { tier = "XS", years = 1, extraSlots = 0 });
        HttpResponseMessage otherTier = await QuoteAsync(client, new { tier = "XS_MAX", years = 1, extraSlots = 0 });

        // Kiểm tra: hạng bị tắt bị chặn ở cả báo giá lẫn tạo đơn, không tạo đơn; hạng còn bán vẫn báo giá được
        JsonElement quoteProblem = await ApiTestHelpers.AssertProblemAsync(quote, HttpStatusCode.Conflict, "PAYMENT_NOT_ALLOWED");
        Assert.Equal("TIER_NOT_ON_SALE", quoteProblem.GetProperty("reason").GetString());
        JsonElement orderProblem = await ApiTestHelpers.AssertProblemAsync(order, HttpStatusCode.Conflict, "PAYMENT_NOT_ALLOWED");
        Assert.Equal("TIER_NOT_ON_SALE", orderProblem.GetProperty("reason").GetString());
        Assert.Equal(0, await CountPaymentOrdersAsync());
        Assert.Equal(HttpStatusCode.OK, otherTier.StatusCode);
    }

    [SqlFact]
    public async Task Sec83_Quote_PaidVaultTierTurnedOff_AddonAndRenewalBlocked()
    {
        // Chuẩn bị: kho đang dùng XS còn hạn, rồi Admin tắt mở bán XS
        (HttpClient client, Guid vaultId, _) = await CreateFreeVaultAsync(ekycDaysAgo: 1);
        await MakePaidActiveAsync(vaultId, daysLeft: 180);
        await SetTierOnSaleOffAsync("Plans.Xs.On");

        // Hành động
        HttpResponseMessage addon = await QuoteAsync(client, new { extraSlots = 1 });
        HttpResponseMessage renewal = await QuoteAsync(client, new { tier = "XS", years = 1, extraSlots = 0, renew = true });

        // Kiểm tra: cả mua thêm suất lẫn gia hạn đều bị chặn, kể cả người đang dùng
        JsonElement problem = await ApiTestHelpers.AssertProblemAsync(addon, HttpStatusCode.Conflict, "PAYMENT_NOT_ALLOWED");
        Assert.Equal("TIER_NOT_ON_SALE", problem.GetProperty("reason").GetString());
        JsonElement renewalProblem = await ApiTestHelpers.AssertProblemAsync(renewal, HttpStatusCode.Conflict, "PAYMENT_NOT_ALLOWED");
        Assert.Equal("TIER_NOT_ON_SALE", renewalProblem.GetProperty("reason").GetString());
    }

    [SqlFact]
    public async Task Sec83_Quote_FreeVaultWithoutEkyc_OwnerEkycRequired()
    {
        // Chuẩn bị
        (HttpClient client, _, _) = await CreateFreeVaultAsync(ekycDaysAgo: null);

        // Hành động
        HttpResponseMessage response = await QuoteAsync(client, new { tier = "XS", years = 1, extraSlots = 0 });

        // Kiểm tra
        JsonElement problem = await ApiTestHelpers.AssertProblemAsync(response, HttpStatusCode.Conflict, "PAYMENT_NOT_ALLOWED");
        Assert.Equal("OWNER_EKYC_REQUIRED", problem.GetProperty("reason").GetString());
    }

    [SqlFact]
    public async Task Sec83_Quote_EkycOlderThanNinetyDays_OwnerEkycRequired()
    {
        // Chuẩn bị
        (HttpClient client, _, _) = await CreateFreeVaultAsync(ekycDaysAgo: 91);

        // Hành động
        HttpResponseMessage response = await QuoteAsync(client, new { tier = "XS", years = 1, extraSlots = 0 });

        // Kiểm tra
        JsonElement problem = await ApiTestHelpers.AssertProblemAsync(response, HttpStatusCode.Conflict, "PAYMENT_NOT_ALLOWED");
        Assert.Equal("OWNER_EKYC_REQUIRED", problem.GetProperty("reason").GetString());
    }

    [SqlFact]
    public async Task Sec83_Quote_EkycFailed_OwnerEkycRequired()
    {
        // Chuẩn bị: lượt eKYC mới nhất của Owner không đạt
        (HttpClient client, _, Guid ownerId) = await CreateFreeVaultAsync(ekycDaysAgo: null);
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            EkycVerification verification = new EkycVerification();
            verification.UserId = ownerId;
            verification.Purpose = EkycPurpose.OwnerEnrollment;
            verification.Status = EkycStatus.Failed;
            verification.CreatedAt = DateTimeOffset.UtcNow;
            verification.CompletedAt = DateTimeOffset.UtcNow;
            db.EkycVerifications.Add(verification);
            await db.SaveChangesAsync();
        });

        // Hành động
        HttpResponseMessage response = await QuoteAsync(client, new { tier = "XS", years = 1, extraSlots = 0 });

        // Kiểm tra
        JsonElement problem = await ApiTestHelpers.AssertProblemAsync(response, HttpStatusCode.Conflict, "PAYMENT_NOT_ALLOWED");
        Assert.Equal("OWNER_EKYC_REQUIRED", problem.GetProperty("reason").GetString());
    }

    [SqlFact]
    public async Task Sec83_Quote_EkycForOtherPurpose_DoesNotCount()
    {
        // Chuẩn bị: eKYC đạt nhưng mục đích BeneficiaryClaim (EKYC-01)
        (HttpClient client, _, Guid ownerId) = await CreateFreeVaultAsync(ekycDaysAgo: null);
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            EkycVerification verification = new EkycVerification();
            verification.UserId = ownerId;
            verification.Purpose = EkycPurpose.BeneficiaryClaim;
            verification.Status = EkycStatus.Passed;
            verification.CreatedAt = DateTimeOffset.UtcNow;
            verification.CompletedAt = DateTimeOffset.UtcNow;
            db.EkycVerifications.Add(verification);
            await db.SaveChangesAsync();
        });

        // Hành động
        HttpResponseMessage response = await QuoteAsync(client, new { tier = "XS", years = 1, extraSlots = 0 });

        // Kiểm tra
        JsonElement problem = await ApiTestHelpers.AssertProblemAsync(response, HttpStatusCode.Conflict, "PAYMENT_NOT_ALLOWED");
        Assert.Equal("OWNER_EKYC_REQUIRED", problem.GetProperty("reason").GetString());
    }

    [SqlFact]
    public async Task Sec83_Quote_PlanLocked_ReturnsPlanLocked()
    {
        PlanStatus[] lockedStatuses = new PlanStatus[]
        {
            PlanStatus.DeathCaseOpen, PlanStatus.HandoverInProgress, PlanStatus.Blocked,
        };
        foreach (PlanStatus status in lockedStatuses)
        {
            // Chuẩn bị
            (HttpClient client, Guid vaultId, _) = await CreateFreeVaultAsync(ekycDaysAgo: 1);
            await ApiTestHelpers.SetPlanStatusAsync(factory, vaultId, status);

            // Hành động
            HttpResponseMessage response = await QuoteAsync(client, new { tier = "XS", years = 1, extraSlots = 0 });

            // Kiểm tra
            JsonElement problem = await ApiTestHelpers.AssertProblemAsync(response, HttpStatusCode.Conflict, "PAYMENT_NOT_ALLOWED");
            Assert.Equal("PLAN_LOCKED", problem.GetProperty("reason").GetString());
        }
    }

    [SqlFact]
    public async Task Ac104_Quote_ClosedVault_ReturnsVaultClosed()
    {
        // Chuẩn bị
        (HttpClient client, Guid vaultId, _) = await CreateFreeVaultAsync(ekycDaysAgo: 1);
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            await db.Vaults.Where(v => v.Id == vaultId)
                .ExecuteUpdateAsync(s => s.SetProperty(v => v.Status, VaultStatus.Closed));
        });

        // Hành động
        HttpResponseMessage response = await QuoteAsync(client, new { tier = "XS", years = 1, extraSlots = 0 });

        // Kiểm tra
        JsonElement problem = await ApiTestHelpers.AssertProblemAsync(response, HttpStatusCode.Conflict, "PAYMENT_NOT_ALLOWED");
        Assert.Equal("VAULT_CLOSED", problem.GetProperty("reason").GetString());
    }

    [SqlFact]
    public async Task Sec83_Quote_GraceReadOnly_IsRenewalStartingOnPayment()
    {
        // Chuẩn bị
        (HttpClient client, Guid vaultId, _) = await CreateFreeVaultAsync(ekycDaysAgo: 1);
        await MakePaidActiveAsync(vaultId, daysLeft: 30);
        await ApiTestHelpers.SetEntitlementStatusAsync(factory, vaultId, EntitlementStatus.GraceReadOnly);

        // Hành động
        HttpResponseMessage response = await QuoteAsync(client, new { tier = "XS", years = 1, extraSlots = 0 });

        // Kiểm tra: ân hạn thì gia hạn, kỳ mới tính từ lúc thanh toán
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        JsonElement quote = await ApiTestHelpers.ReadJsonAsync(response);
        Assert.Equal("RENEWAL", quote.GetProperty("kind").GetString());
        Assert.Equal("ON_PAYMENT", quote.GetProperty("serviceStartsAt").GetString());
        Assert.Equal(199000, quote.GetProperty("totalAmount").GetInt64());
    }

    [SqlFact]
    public async Task Sec83_Quote_PaidActiveButExpired_IsRenewalAndNeedsTier()
    {
        // Chuẩn bị: còn PaidActive nhưng đã quá hạn (job chuyển ân hạn chưa chạy)
        (HttpClient client, Guid vaultId, _) = await CreateFreeVaultAsync(ekycDaysAgo: 1);
        await MakePaidActiveAsync(vaultId, daysLeft: -1);

        // Hành động: hết hạn nên chỉ còn gia hạn; không gửi hạng và số năm
        HttpResponseMessage response = await QuoteAsync(client, new { extraSlots = 1 });

        // Kiểm tra: thiếu hạng là lỗi dữ liệu, không còn bị từ chối vì "chưa hỗ trợ gia hạn"
        await ApiTestHelpers.AssertProblemAsync(response, HttpStatusCode.BadRequest, "VALIDATION_ERROR");
    }

    [SqlFact]
    public async Task Ac95_Quote_AddonWith180DaysLeft_Is24165_IgnoresTierAndYears()
    {
        // Chuẩn bị: XS còn 180 ngày; không cần eKYC cho ADDON
        (HttpClient client, Guid vaultId, _) = await CreateFreeVaultAsync(ekycDaysAgo: null);
        await MakePaidActiveAsync(vaultId, daysLeft: 180);

        // Hành động: tier và years gửi vào bị bỏ qua
        HttpResponseMessage response = await QuoteAsync(client, new { tier = "XS_MAX", years = 5, extraSlots = 1 });

        // Kiểm tra
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        JsonElement quote = await ApiTestHelpers.ReadJsonAsync(response);
        Assert.Equal("ADDON", quote.GetProperty("kind").GetString());
        Assert.Equal("XS", quote.GetProperty("tier").GetString());
        Assert.Equal(JsonValueKind.Null, quote.GetProperty("years").ValueKind);
        Assert.Equal(0, quote.GetProperty("tierAmount").GetInt64());
        Assert.Equal(24165, quote.GetProperty("totalAmount").GetInt64());
        Assert.Equal(3, quote.GetProperty("beneficiarySlots").GetInt32());
        Assert.Equal(JsonValueKind.Null, quote.GetProperty("serviceStartsAt").ValueKind);
        Assert.NotEqual(JsonValueKind.Null, quote.GetProperty("serviceExpiresAt").ValueKind);
        Assert.Equal(0, await CountPaymentOrdersAsync());
    }

    [SqlFact]
    public async Task Addon01_Quote_PurchaseExtraSlotsTwentyOne_Returns400()
    {
        // Chuẩn bị
        (HttpClient client, _, _) = await CreateFreeVaultAsync(ekycDaysAgo: 1);

        // Hành động
        HttpResponseMessage response = await QuoteAsync(client, new { tier = "XS", years = 1, extraSlots = 21 });

        // Kiểm tra
        JsonElement problem = await ApiTestHelpers.AssertProblemAsync(response, HttpStatusCode.BadRequest, "VALIDATION_ERROR");
        Assert.True(problem.GetProperty("errors").TryGetProperty("extraSlots", out _));
    }

    [SqlFact]
    public async Task Sec81_Quote_YearsTwo_Returns400()
    {
        // Chuẩn bị
        (HttpClient client, _, _) = await CreateFreeVaultAsync(ekycDaysAgo: 1);

        // Hành động
        HttpResponseMessage response = await QuoteAsync(client, new { tier = "XS", years = 2, extraSlots = 0 });

        // Kiểm tra
        JsonElement problem = await ApiTestHelpers.AssertProblemAsync(response, HttpStatusCode.BadRequest, "VALIDATION_ERROR");
        Assert.True(problem.GetProperty("errors").TryGetProperty("years", out _));
    }

    [SqlFact]
    public async Task Sec81_Quote_TierFree_Returns400()
    {
        // Chuẩn bị
        (HttpClient client, _, _) = await CreateFreeVaultAsync(ekycDaysAgo: 1);

        // Hành động
        HttpResponseMessage response = await QuoteAsync(client, new { tier = "FREE", years = 1, extraSlots = 0 });

        // Kiểm tra
        JsonElement problem = await ApiTestHelpers.AssertProblemAsync(response, HttpStatusCode.BadRequest, "VALIDATION_ERROR");
        Assert.True(problem.GetProperty("errors").TryGetProperty("tier", out _));
    }

    [SqlFact]
    public async Task Addon01_Quote_AddonWithZeroSlots_Returns400()
    {
        // Chuẩn bị
        (HttpClient client, Guid vaultId, _) = await CreateFreeVaultAsync(ekycDaysAgo: null);
        await MakePaidActiveAsync(vaultId, daysLeft: 100);

        // Hành động
        HttpResponseMessage response = await QuoteAsync(client, new { extraSlots = 0 });

        // Kiểm tra
        JsonElement problem = await ApiTestHelpers.AssertProblemAsync(response, HttpStatusCode.BadRequest, "VALIDATION_ERROR");
        Assert.True(problem.GetProperty("errors").TryGetProperty("extraSlots", out _));
    }

    [SqlFact]
    public async Task Setup12_Quote_UserWithoutVault_Returns404()
    {
        // Chuẩn bị: user chưa tạo kho
        HttpClient client = await ApiTestHelpers.CreateClientForNewUserAsync(factory, emailVerified: true);

        // Hành động
        HttpResponseMessage response = await QuoteAsync(client, new { tier = "XS", years = 1, extraSlots = 0 });

        // Kiểm tra
        await ApiTestHelpers.AssertProblemAsync(response, HttpStatusCode.NotFound, "VAULT_NOT_FOUND");
    }
}
