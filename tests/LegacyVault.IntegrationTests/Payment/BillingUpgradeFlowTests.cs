using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;
using LegacyVault.IntegrationTests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace LegacyVault.IntegrationTests.Payment;

/// <summary>Nâng hạng giữa kỳ XS → XS_MAX trên API thật và SQL Server tạm: báo giá, tạo đơn, dev-pay.</summary>
public sealed class BillingUpgradeFlowTests : IAsyncLifetime
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

    private async Task<VaultEntitlement> LoadEntitlementAsync(Guid vaultId)
    {
        VaultEntitlement? entitlement = null;
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            entitlement = await db.VaultEntitlements.AsNoTracking().SingleAsync(e => e.VaultId == vaultId);
        });
        return entitlement!;
    }

    private static Task<HttpResponseMessage> QuoteUpgradeAsync(HttpClient client)
    {
        return client.PostAsJsonAsync("/api/v1/billing/quote", new { tier = "XS_MAX", extraSlots = 0 });
    }

    private static async Task<Guid> CreateUpgradeOrderAsync(HttpClient client)
    {
        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/v1/billing/orders", new { tier = "XS_MAX", extraSlots = 0 });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await ApiTestHelpers.ReadJsonAsync(response)).GetProperty("id").GetGuid();
    }

    [SqlFact]
    public async Task Upgrade_Quote_PaidXsWithoutPurchaseOrder_UsesOneYearPricesAndDoesNotWriteAnything()
    {
        // Chuẩn bị: XS còn 180 ngày (kho dựng bằng SQL nên không có đơn mua → bảng giá 1 năm)
        (HttpClient client, Guid vaultId, _) = await BillingTestData.CreateFreeVaultAsync(factory, null);
        await BillingTestData.MakePaidActiveAsync(factory, vaultId, TimeSpan.FromDays(180));

        // Hành động
        HttpResponseMessage response = await QuoteUpgradeAsync(client);

        // Kiểm tra: 200.000 × 180/365 ≈ 98.630, hạng XS_MAX, 500 MiB, 5 suất kèm, không ghi đơn nào
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        JsonElement quote = await ApiTestHelpers.ReadJsonAsync(response);
        Assert.Equal("UPGRADE", quote.GetProperty("kind").GetString());
        Assert.Equal("XS_MAX", quote.GetProperty("tier").GetString());
        long total = quote.GetProperty("totalAmount").GetInt64();
        Assert.InRange(total, 98000, 99000);
        Assert.Equal(total, quote.GetProperty("tierAmount").GetInt64());
        Assert.Equal(0, quote.GetProperty("slotAmount").GetInt64());
        Assert.Equal(500 * 1048576L, quote.GetProperty("storageQuotaBytes").GetInt64());
        Assert.Equal(5, quote.GetProperty("beneficiarySlots").GetInt32());
        Assert.Equal(0, await BillingTestData.CountOrdersAsync(factory, vaultId, null));
    }

    [SqlFact]
    public async Task Upgrade_Quote_AfterRealPurchaseOfThreeYears_UsesThreeYearPriceDifference()
    {
        // Chuẩn bị: mua XS 3 năm thật rồi trả bằng dev-pay (kho vừa mua nên còn gần đủ 1.095 ngày)
        (HttpClient client, _, _) = await BillingTestData.CreateFreeVaultAsync(factory, ekycDaysAgo: 1);
        HttpResponseMessage create = await client.PostAsJsonAsync(
            "/api/v1/billing/orders", new { tier = "XS", years = 3, extraSlots = 0 });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        Guid purchaseId = (await ApiTestHelpers.ReadJsonAsync(create)).GetProperty("id").GetGuid();
        HttpResponseMessage paid = await client.PostAsync($"/api/v1/billing/orders/{purchaseId}/dev-pay", null);
        Assert.Equal(HttpStatusCode.OK, paid.StatusCode);

        // Hành động
        HttpResponseMessage response = await QuoteUpgradeAsync(client);

        // Kiểm tra: chênh giá 3 năm là 500.000, còn gần đủ kỳ nên gần 500.000 (không phải 200.000 của bảng giá 1 năm)
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        long total = (await ApiTestHelpers.ReadJsonAsync(response)).GetProperty("totalAmount").GetInt64();
        Assert.InRange(total, 499000, 500000);
    }

    [SqlFact]
    public async Task Upgrade_CreateOrderAndDevPay_GrantsXsMaxKeepsExpiryAndExtraSlots()
    {
        // Chuẩn bị: XS còn 90 ngày, đã có 1 suất mua thêm
        (HttpClient client, Guid vaultId, _) = await BillingTestData.CreateFreeVaultAsync(factory, null);
        await BillingTestData.MakePaidActiveAsync(factory, vaultId, TimeSpan.FromDays(90));
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            await db.VaultEntitlements.Where(e => e.VaultId == vaultId)
                .ExecuteUpdateAsync(s => s.SetProperty(e => e.ExtraBeneficiarySlots, 1));
        });
        VaultEntitlement before = await LoadEntitlementAsync(vaultId);
        Guid orderId = await CreateUpgradeOrderAsync(client);

        // Hành động
        HttpResponseMessage response = await client.PostAsync($"/api/v1/billing/orders/{orderId}/dev-pay", null);

        // Kiểm tra
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        JsonElement order = await ApiTestHelpers.ReadJsonAsync(response);
        Assert.Equal("PAID", order.GetProperty("status").GetString());
        Assert.Equal("UPGRADE", order.GetProperty("kind").GetString());
        VaultEntitlement after = await LoadEntitlementAsync(vaultId);
        Assert.Equal(ServiceTier.XsMax, after.Tier);
        Assert.Equal(EntitlementStatus.PaidActive, after.Status);
        Assert.Equal(500 * 1048576L, after.StorageQuotaBytes);
        Assert.Equal(5, after.IncludedBeneficiarySlots);
        Assert.Equal(1, after.ExtraBeneficiarySlots);
        Assert.Equal(before.ServiceExpiresAt, after.ServiceExpiresAt);
    }

    [SqlFact]
    public async Task Upgrade_XsMaxChoiceWithExtraSlots_StaysAddonAndIgnoresTier()
    {
        // Chuẩn bị
        (HttpClient client, Guid vaultId, _) = await BillingTestData.CreateFreeVaultAsync(factory, null);
        await BillingTestData.MakePaidActiveAsync(factory, vaultId, TimeSpan.FromDays(90));

        // Hành động: có mua thêm suất thì không phải nâng hạng (hợp đồng cũ: tier bị bỏ qua)
        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/v1/billing/quote", new { tier = "XS_MAX", extraSlots = 1 });

        // Kiểm tra
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        JsonElement quote = await ApiTestHelpers.ReadJsonAsync(response);
        Assert.Equal("ADDON", quote.GetProperty("kind").GetString());
        Assert.Equal("XS", quote.GetProperty("tier").GetString());
    }

    [SqlFact]
    public async Task Upgrade_XsMaxVaultChoosingXsMax_IsAddonNotUpgrade()
    {
        // Chuẩn bị: đã ở XS_MAX
        (HttpClient client, Guid vaultId, _) = await BillingTestData.CreateFreeVaultAsync(factory, null);
        await BillingTestData.MakePaidActiveAsync(factory, vaultId, TimeSpan.FromDays(90));
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            await db.VaultEntitlements.Where(e => e.VaultId == vaultId)
                .ExecuteUpdateAsync(s => s.SetProperty(e => e.Tier, ServiceTier.XsMax));
        });

        // Hành động
        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/v1/billing/quote", new { tier = "XS_MAX", extraSlots = 1 });

        // Kiểm tra: không có đường nâng hạng nữa, chỉ mua thêm suất
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("ADDON", (await ApiTestHelpers.ReadJsonAsync(response)).GetProperty("kind").GetString());
    }

    [SqlFact]
    public async Task Upgrade_ExpiredVault_IsRenewalAndNeedsYears()
    {
        // Chuẩn bị: kho đang ân hạn
        (HttpClient client, Guid vaultId, _) = await BillingTestData.CreateFreeVaultAsync(factory, null);
        await BillingTestData.MakePaidActiveAsync(factory, vaultId, TimeSpan.FromDays(90));
        await ApiTestHelpers.SetEntitlementStatusAsync(factory, vaultId, EntitlementStatus.GraceReadOnly);

        // Hành động
        HttpResponseMessage response = await QuoteUpgradeAsync(client);

        // Kiểm tra: kho ân hạn chỉ còn gia hạn, nên thiếu số năm là lỗi dữ liệu (không phải nâng hạng)
        await ApiTestHelpers.AssertProblemAsync(response, HttpStatusCode.BadRequest, "VALIDATION_ERROR");
    }

    [SqlFact]
    public async Task Upgrade_DevPay_TermChangedAfterOrderCreated_Returns409AndGrantsNothing()
    {
        // Chuẩn bị: tạo đơn nâng hạng, sau đó hạn của kho bị đổi
        (HttpClient client, Guid vaultId, _) = await BillingTestData.CreateFreeVaultAsync(factory, null);
        await BillingTestData.MakePaidActiveAsync(factory, vaultId, TimeSpan.FromDays(90));
        Guid orderId = await CreateUpgradeOrderAsync(client);
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            DateTimeOffset changed = DateTimeOffset.UtcNow.AddDays(200);
            await db.VaultEntitlements.Where(e => e.VaultId == vaultId)
                .ExecuteUpdateAsync(s => s.SetProperty(e => e.ServiceExpiresAt, changed));
        });

        // Hành động
        HttpResponseMessage response = await client.PostAsync($"/api/v1/billing/orders/{orderId}/dev-pay", null);

        // Kiểm tra: không đổi hạng
        JsonElement problem = await ApiTestHelpers.AssertProblemAsync(
            response, HttpStatusCode.Conflict, "PAYMENT_ORDER_NOT_PAYABLE");
        Assert.Equal("ENTITLEMENT_CHANGED", problem.GetProperty("reason").GetString());
        Assert.Equal(ServiceTier.Xs, (await LoadEntitlementAsync(vaultId)).Tier);
    }

    [SqlFact]
    public async Task Upgrade_CreateTwiceSameSelection_ReturnsSameOrder()
    {
        // Chuẩn bị
        (HttpClient client, Guid vaultId, _) = await BillingTestData.CreateFreeVaultAsync(factory, null);
        await BillingTestData.MakePaidActiveAsync(factory, vaultId, TimeSpan.FromDays(90));
        Guid first = await CreateUpgradeOrderAsync(client);

        // Hành động: gửi lại cùng lựa chọn
        HttpResponseMessage again = await client.PostAsJsonAsync(
            "/api/v1/billing/orders", new { tier = "XS_MAX", extraSlots = 0 });

        // Kiểm tra: 200 trả lại đơn cũ, chỉ có một đơn
        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
        Assert.Equal(first, (await ApiTestHelpers.ReadJsonAsync(again)).GetProperty("id").GetGuid());
        Assert.Equal(1, await BillingTestData.CountOrdersAsync(factory, vaultId, null));
    }
}
