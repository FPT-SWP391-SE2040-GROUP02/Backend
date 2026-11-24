using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;
using LegacyVault.IntegrationTests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace LegacyVault.IntegrationTests.Payment;

/// <summary>Gia hạn (RENEWAL) trên API thật và SQL Server tạm: gia hạn sớm, gia hạn muộn, đổi hạng, kiểm tra dữ liệu đang dùng.</summary>
public sealed class BillingRenewalFlowTests : IAsyncLifetime
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

    private static Task<HttpResponseMessage> QuoteAsync(HttpClient client, object body)
    {
        return client.PostAsJsonAsync("/api/v1/billing/quote", body);
    }

    private static async Task<Guid> CreateOrderAsync(HttpClient client, object body)
    {
        HttpResponseMessage response = await client.PostAsJsonAsync("/api/v1/billing/orders", body);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await ApiTestHelpers.ReadJsonAsync(response)).GetProperty("id").GetGuid();
    }

    private static async Task DevPayOkAsync(HttpClient client, Guid orderId)
    {
        HttpResponseMessage response = await client.PostAsync($"/api/v1/billing/orders/{orderId}/dev-pay", null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [SqlFact]
    public async Task Renewal_EarlyQuote_WithRenewFlag_ContinuesFromCurrentExpiry()
    {
        // Chuẩn bị: XS còn 100 ngày
        (HttpClient client, Guid vaultId, _) = await BillingTestData.CreateFreeVaultAsync(factory, null);
        await BillingTestData.MakePaidActiveAsync(factory, vaultId, TimeSpan.FromDays(100));

        // Hành động
        HttpResponseMessage response = await QuoteAsync(
            client, new { tier = "XS", years = 1, renew = true });

        // Kiểm tra: gia hạn sớm, nối tiếp hạn cũ, không ghi đơn nào
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        JsonElement quote = await ApiTestHelpers.ReadJsonAsync(response);
        Assert.Equal("RENEWAL", quote.GetProperty("kind").GetString());
        Assert.Equal("AFTER_CURRENT_TERM", quote.GetProperty("serviceStartsAt").GetString());
        Assert.NotEqual(JsonValueKind.Null, quote.GetProperty("serviceExpiresAt").ValueKind);
        Assert.Equal(199000, quote.GetProperty("totalAmount").GetInt64());
        Assert.Equal(0, await BillingTestData.CountOrdersAsync(factory, vaultId, null));
    }

    [SqlFact]
    public async Task Renewal_ActiveVaultWithoutRenewFlag_NothingToBuy()
    {
        // Chuẩn bị
        (HttpClient client, Guid vaultId, _) = await BillingTestData.CreateFreeVaultAsync(factory, null);
        await BillingTestData.MakePaidActiveAsync(factory, vaultId, TimeSpan.FromDays(100));

        // Hành động: kho còn hạn, không gửi renew và không chọn gói cao hơn
        HttpResponseMessage response = await QuoteAsync(client, new { tier = "XS", years = 1 });

        // Kiểm tra
        JsonElement problem = await ApiTestHelpers.AssertProblemAsync(
            response, HttpStatusCode.Conflict, "PAYMENT_NOT_ALLOWED");
        Assert.Equal("NOTHING_TO_BUY", problem.GetProperty("reason").GetString());
    }

    [SqlFact]
    public async Task Renewal_EarlyDevPay_ExtendsExpiryByFullTermWithoutLosingRemainingDays()
    {
        // Chuẩn bị: XS còn 100 ngày, gia hạn 1 năm
        (HttpClient client, Guid vaultId, _) = await BillingTestData.CreateFreeVaultAsync(factory, null);
        await BillingTestData.MakePaidActiveAsync(factory, vaultId, TimeSpan.FromDays(100));
        VaultEntitlement before = await LoadEntitlementAsync(vaultId);
        Guid orderId = await CreateOrderAsync(client, new { tier = "XS", years = 1, renew = true });

        // Hành động
        await DevPayOkAsync(client, orderId);

        // Kiểm tra: hạn mới = hạn cũ + 365 ngày
        VaultEntitlement after = await LoadEntitlementAsync(vaultId);
        Assert.Equal(before.ServiceExpiresAt!.Value.AddDays(365), after.ServiceExpiresAt);
        Assert.Equal(EntitlementStatus.PaidActive, after.Status);
        Assert.Equal(ServiceTier.Xs, after.Tier);
    }

    [SqlFact]
    public async Task Renewal_LateAfterGrace_DevPayStartsFromPaymentAndRestoresPaidActive()
    {
        // Chuẩn bị: kho đang ân hạn, hạn cũ cách đây 30 ngày
        (HttpClient client, Guid vaultId, _) = await BillingTestData.CreateFreeVaultAsync(factory, null);
        await BillingTestData.MakePaidActiveAsync(factory, vaultId, TimeSpan.FromDays(-30));
        await ApiTestHelpers.SetEntitlementStatusAsync(factory, vaultId, EntitlementStatus.GraceReadOnly);
        Guid orderId = await CreateOrderAsync(client, new { tier = "XS", years = 1 });
        DateTimeOffset beforePay = DateTimeOffset.UtcNow;

        // Hành động
        await DevPayOkAsync(client, orderId);

        // Kiểm tra: kỳ mới tính từ lúc trả (không nối vào hạn cũ), trạng thái về PaidActive
        VaultEntitlement after = await LoadEntitlementAsync(vaultId);
        Assert.Equal(EntitlementStatus.PaidActive, after.Status);
        Assert.InRange(
            after.ServiceExpiresAt!.Value, beforePay.AddDays(365).AddMinutes(-1), DateTimeOffset.UtcNow.AddDays(365).AddMinutes(1));
    }

    [SqlFact]
    public async Task Renewal_ExpiredReviewVault_CanRenewAndDoesNotNeedEkyc()
    {
        // Chuẩn bị: hết ân hạn, chờ rà soát; Chủ kho chưa có eKYC
        (HttpClient client, Guid vaultId, _) = await BillingTestData.CreateFreeVaultAsync(factory, null);
        await BillingTestData.MakePaidActiveAsync(factory, vaultId, TimeSpan.FromDays(-300));
        await ApiTestHelpers.SetEntitlementStatusAsync(factory, vaultId, EntitlementStatus.ExpiredReview);

        // Hành động
        HttpResponseMessage response = await QuoteAsync(client, new { tier = "XS", years = 1 });

        // Kiểm tra: gia hạn không đòi eKYC (chỉ mua mới mới đòi)
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("RENEWAL", (await ApiTestHelpers.ReadJsonAsync(response)).GetProperty("kind").GetString());
    }

    [SqlFact]
    public async Task Renewal_AfterExpiry_ChooseHigherTier_GrantsXsMaxFromPayment()
    {
        // Chuẩn bị: kho XS đang ân hạn, gia hạn 1 năm lên XS Max
        (HttpClient client, Guid vaultId, _) = await BillingTestData.CreateFreeVaultAsync(factory, null);
        await BillingTestData.MakePaidActiveAsync(factory, vaultId, TimeSpan.FromDays(-30));
        await ApiTestHelpers.SetEntitlementStatusAsync(factory, vaultId, EntitlementStatus.GraceReadOnly);
        Guid orderId = await CreateOrderAsync(client, new { tier = "XS_MAX", years = 1 });

        // Hành động
        await DevPayOkAsync(client, orderId);

        // Kiểm tra
        VaultEntitlement after = await LoadEntitlementAsync(vaultId);
        Assert.Equal(ServiceTier.XsMax, after.Tier);
        Assert.Equal(500 * 1048576L, after.StorageQuotaBytes);
        Assert.Equal(5, after.IncludedBeneficiarySlots);
        Assert.Equal(EntitlementStatus.PaidActive, after.Status);
    }

    [SqlFact]
    public async Task Renewal_EarlyRenewalWithDifferentTier_Returns400AndWritesNothing()
    {
        // Chuẩn bị: XS còn 30 ngày
        (HttpClient client, Guid vaultId, _) = await BillingTestData.CreateFreeVaultAsync(factory, null);
        await BillingTestData.MakePaidActiveAsync(factory, vaultId, TimeSpan.FromDays(30));

        // Hành động: gia hạn sớm nhưng chọn XS Max (phải dùng nâng hạng để trả phần chênh)
        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/v1/billing/orders", new { tier = "XS_MAX", years = 1, renew = true });

        // Kiểm tra
        await ApiTestHelpers.AssertProblemAsync(response, HttpStatusCode.BadRequest, "VALIDATION_ERROR");
        Assert.Equal(0, await BillingTestData.CountOrdersAsync(factory, vaultId, null));
        Assert.Equal(ServiceTier.Xs, (await LoadEntitlementAsync(vaultId)).Tier);
    }

    [SqlFact]
    public async Task Renewal_ToSmallerPlanThanBeneficiariesInUse_Returns409UsageExceedsPlan()
    {
        // Chuẩn bị: XS Max (5 suất) đang dùng 3 suất người nhận (một tài sản gán chung cho 3 người)
        (HttpClient client, Guid vaultId, _) = await BillingTestData.CreateFreeVaultAsync(factory, null);
        await BillingTestData.MakePaidActiveAsync(factory, vaultId, TimeSpan.FromDays(-10));
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            await db.VaultEntitlements.Where(e => e.VaultId == vaultId)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(e => e.Tier, ServiceTier.XsMax)
                    .SetProperty(e => e.StorageQuotaBytes, 500 * 1048576L)
                    .SetProperty(e => e.IncludedBeneficiarySlots, 5));
        });
        Guid assetId = await ApiTestHelpers.CreateTextAssetAsync(client, vaultId, "Ghi chú thử");
        List<Guid> beneficiaryIds = new List<Guid>();
        string[] citizenIds = new string[] { "001090000001", "001090000002", "001090000003" };
        for (int i = 0; i < citizenIds.Length; i++)
        {
            JsonElement beneficiary = await ApiTestHelpers.CreateBeneficiaryAsync(
                client, vaultId, citizenIds[i], ApiTestHelpers.BeneficiaryName(i));
            beneficiaryIds.Add(beneficiary.GetProperty("id").GetGuid());
        }
        await ApiTestHelpers.AssignAssetOkAsync(client, vaultId, assetId, beneficiaryIds.ToArray());

        // Hành động: gia hạn xuống XS (2 suất kèm theo) hoặc giữ XS Max (5 suất kèm theo)
        HttpResponseMessage tooSmall = await QuoteAsync(
            client, new { tier = "XS", years = 1, renew = true });
        HttpResponseMessage enough = await QuoteAsync(
            client, new { tier = "XS_MAX", years = 1, renew = true });

        // Kiểm tra: 3 suất đang dùng > 2 thì chặn; XS Max đủ 5 suất thì được
        JsonElement problem = await ApiTestHelpers.AssertProblemAsync(
            tooSmall, HttpStatusCode.Conflict, "PAYMENT_NOT_ALLOWED");
        Assert.Equal("USAGE_EXCEEDS_PLAN", problem.GetProperty("reason").GetString());
        Assert.Equal(HttpStatusCode.OK, enough.StatusCode);
        JsonElement quote = await ApiTestHelpers.ReadJsonAsync(enough);
        Assert.Equal(5, quote.GetProperty("includedBeneficiarySlots").GetInt32());
        Assert.Equal(0, await BillingTestData.CountOrdersAsync(factory, vaultId, null));
    }

    [SqlFact]
    public async Task Renewal_DevPay_TermChangedAfterEarlyOrder_Returns409AndGrantsNothing()
    {
        // Chuẩn bị: đơn gia hạn sớm, sau đó hạn của kho bị đổi
        (HttpClient client, Guid vaultId, _) = await BillingTestData.CreateFreeVaultAsync(factory, null);
        await BillingTestData.MakePaidActiveAsync(factory, vaultId, TimeSpan.FromDays(60));
        Guid orderId = await CreateOrderAsync(client, new { tier = "XS", years = 1, renew = true });
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            DateTimeOffset changed = DateTimeOffset.UtcNow.AddDays(300);
            await db.VaultEntitlements.Where(e => e.VaultId == vaultId)
                .ExecuteUpdateAsync(s => s.SetProperty(e => e.ServiceExpiresAt, changed));
        });
        VaultEntitlement before = await LoadEntitlementAsync(vaultId);

        // Hành động
        HttpResponseMessage response = await client.PostAsync($"/api/v1/billing/orders/{orderId}/dev-pay", null);

        // Kiểm tra
        JsonElement problem = await ApiTestHelpers.AssertProblemAsync(
            response, HttpStatusCode.Conflict, "PAYMENT_ORDER_NOT_PAYABLE");
        Assert.Equal("ENTITLEMENT_CHANGED", problem.GetProperty("reason").GetString());
        Assert.Equal(before.ServiceExpiresAt, (await LoadEntitlementAsync(vaultId)).ServiceExpiresAt);
    }
}
