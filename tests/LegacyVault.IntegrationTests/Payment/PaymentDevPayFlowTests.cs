using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;
using LegacyVault.IntegrationTests.TestSupport;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace LegacyVault.IntegrationTests.Payment;

/// <summary>
/// Cấp quyền dịch vụ khi thanh toán qua endpoint giả lập dev-pay (SRS §8.3, D8, D9, AC-90, AC-95, PAY-04).
/// dev-pay gọi đúng phần cấp quyền mà webhook SePay (H2) sẽ gọi.
/// </summary>
public sealed class PaymentDevPayFlowTests : IAsyncLifetime
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

    private static async Task<Guid> CreateXsOrderAsync(HttpClient client)
    {
        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/v1/billing/orders", new { tier = "XS", years = 3, extraSlots = 1 });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await ApiTestHelpers.ReadJsonAsync(response)).GetProperty("id").GetGuid();
    }

    private static async Task<Guid> CreateAddonOrderAsync(HttpClient client, int extraSlots)
    {
        HttpResponseMessage response = await client.PostAsJsonAsync("/api/v1/billing/orders", new { extraSlots });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await ApiTestHelpers.ReadJsonAsync(response)).GetProperty("id").GetGuid();
    }

    private static Task<HttpResponseMessage> DevPayAsync(HttpClient client, Guid orderId)
    {
        return client.PostAsync($"/api/v1/billing/orders/{orderId}/dev-pay", null);
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

    private async Task<int> CountPaidLogsAsync(Guid vaultId)
    {
        int count = 0;
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            count = await db.AuditLogs.CountAsync(l => l.VaultId == vaultId && l.Action == AuditAction.PaymentPaid);
        });
        return count;
    }

    [SqlFact]
    public async Task Ac90_Setup14_DevPay_Purchase_GrantsXsAndKeepsPlanDraft()
    {
        // Chuẩn bị: Free + eKYC → đơn XS 3 năm + 1 suất
        (HttpClient client, Guid vaultId, _) = await BillingTestData.CreateFreeVaultAsync(factory, 1);
        Guid orderId = await CreateXsOrderAsync(client);
        int configVersionBefore = await ApiTestHelpers.GetPlanConfigVersionAsync(client);
        DateTimeOffset before = DateTimeOffset.UtcNow;

        // Hành động
        HttpResponseMessage response = await DevPayAsync(client, orderId);

        // Kiểm tra: đơn PAID
        DateTimeOffset after = DateTimeOffset.UtcNow;
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        JsonElement order = await ApiTestHelpers.ReadJsonAsync(response);
        Assert.Equal("PAID", order.GetProperty("status").GetString());
        Assert.NotEqual(JsonValueKind.Null, order.GetProperty("paidAt").ValueKind);
        Assert.Equal(JsonValueKind.Null, order.GetProperty("qrImageUrl").ValueKind);

        // Kiểm tra: quyền dịch vụ tăng, kế hoạch vẫn DRAFT (SETUP-14)
        JsonElement vault = await ApiTestHelpers.ReadJsonAsync(await client.GetAsync("/api/v1/vaults/me"));
        Assert.Equal("XS", vault.GetProperty("serviceTier").GetString());
        Assert.Equal("PAID_ACTIVE", vault.GetProperty("serviceStatus").GetString());
        Assert.Equal(200 * 1048576L, vault.GetProperty("storageQuotaBytes").GetInt64());
        Assert.Equal(2, vault.GetProperty("includedBeneficiarySlots").GetInt32());
        Assert.Equal(1, vault.GetProperty("extraBeneficiarySlots").GetInt32());
        Assert.Equal(3, vault.GetProperty("beneficiarySlots").GetInt32());
        DateTimeOffset serviceExpiresAt = vault.GetProperty("serviceExpiresAt").GetDateTimeOffset();
        Assert.InRange(serviceExpiresAt, before.AddDays(1095).AddSeconds(-1), after.AddDays(1095).AddSeconds(1));
        Assert.Equal("DRAFT", vault.GetProperty("planStatus").GetString());
        Assert.Equal(configVersionBefore, vault.GetProperty("planConfigVersion").GetInt32());

        // Kiểm tra: đơn không còn là đơn hiện tại, có đúng một dòng nhật ký PAYMENT_PAID
        PaymentOrder stored = await BillingTestData.LoadOrderAsync(factory, orderId);
        Assert.Equal(PaymentOrderStatus.Paid, stored.Status);
        Assert.NotNull(stored.PaidAt);
        Assert.Equal(HttpStatusCode.NoContent, (await client.GetAsync("/api/v1/billing/orders/current")).StatusCode);
        Assert.Equal(1, await CountPaidLogsAsync(vaultId));
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            AuditLog log = await db.AuditLogs.SingleAsync(l => l.VaultId == vaultId && l.Action == AuditAction.PaymentPaid);
            Assert.Equal(AuditActorRole.System, log.ActorRole);
            Assert.Null(log.ActorUserId);
            Assert.Equal(orderId, log.TargetId);
        });
    }

    [SqlFact]
    public async Task Ac95_DevPay_Addon_AddsSlotsAndKeepsExpiry()
    {
        // Chuẩn bị: XS còn 180 ngày, chưa có suất thêm
        (HttpClient client, Guid vaultId, _) = await BillingTestData.CreateFreeVaultAsync(factory, null);
        await BillingTestData.MakePaidActiveAsync(factory, vaultId, TimeSpan.FromDays(180));
        VaultEntitlement before = await LoadEntitlementAsync(vaultId);
        Guid orderId = await CreateAddonOrderAsync(client, 1);

        // Hành động
        HttpResponseMessage response = await DevPayAsync(client, orderId);

        // Kiểm tra: +1 suất, hạn và hạng giữ nguyên
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("PAID", (await ApiTestHelpers.ReadJsonAsync(response)).GetProperty("status").GetString());
        VaultEntitlement after = await LoadEntitlementAsync(vaultId);
        Assert.Equal(before.ExtraBeneficiarySlots + 1, after.ExtraBeneficiarySlots);
        Assert.Equal(before.ServiceExpiresAt, after.ServiceExpiresAt);
        Assert.Equal(ServiceTier.Xs, after.Tier);
        Assert.Equal(EntitlementStatus.PaidActive, after.Status);
        Assert.Equal(1, await CountPaidLogsAsync(vaultId));
    }

    [SqlFact]
    public async Task Pay04_DevPay_Repeated_GrantsOnlyOnce()
    {
        // Chuẩn bị
        (HttpClient client, Guid vaultId, _) = await BillingTestData.CreateFreeVaultAsync(factory, 1);
        Guid orderId = await CreateXsOrderAsync(client);
        await DevPayAsync(client, orderId);
        VaultEntitlement afterFirst = await LoadEntitlementAsync(vaultId);

        // Hành động
        HttpResponseMessage second = await DevPayAsync(client, orderId);

        // Kiểm tra: lần hai chỉ trả lại đơn PAID, không cấp thêm
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Equal("PAID", (await ApiTestHelpers.ReadJsonAsync(second)).GetProperty("status").GetString());
        VaultEntitlement afterSecond = await LoadEntitlementAsync(vaultId);
        Assert.Equal(afterFirst.ExtraBeneficiarySlots, afterSecond.ExtraBeneficiarySlots);
        Assert.Equal(afterFirst.ServiceExpiresAt, afterSecond.ServiceExpiresAt);
        Assert.Equal(1, await CountPaidLogsAsync(vaultId));
    }

    [SqlFact]
    public async Task Pay04_DevPay_Concurrent_GrantsOnlyOnce()
    {
        // Chuẩn bị: ADDON để cấp trùng sẽ lộ ngay (suất thêm tăng nhiều hơn 1)
        (HttpClient client, Guid vaultId, _) = await BillingTestData.CreateFreeVaultAsync(factory, null);
        await BillingTestData.MakePaidActiveAsync(factory, vaultId, TimeSpan.FromDays(180));
        Guid orderId = await CreateAddonOrderAsync(client, 1);

        // Hành động: sáu request trả tiền cùng lúc
        List<Task<HttpResponseMessage>> pays = new List<Task<HttpResponseMessage>>();
        for (int i = 0; i < 6; i++)
        {
            pays.Add(DevPayAsync(client, orderId));
        }
        HttpResponseMessage[] responses = await Task.WhenAll(pays);

        // Kiểm tra: mỗi request là 200 hoặc 409 EDIT_CONFLICT (deadlock/RowVersion), ít nhất một 200, cấp đúng một lần
        int ok = 0;
        foreach (HttpResponseMessage response in responses)
        {
            if (response.StatusCode == HttpStatusCode.OK)
            {
                ok++;
            }
            else
            {
                await ApiTestHelpers.AssertProblemAsync(response, HttpStatusCode.Conflict, "EDIT_CONFLICT");
            }
        }
        Assert.True(ok >= 1);
        VaultEntitlement entitlement = await LoadEntitlementAsync(vaultId);
        Assert.Equal(1, entitlement.ExtraBeneficiarySlots);
        Assert.Equal(1, await CountPaidLogsAsync(vaultId));
        Assert.Equal(PaymentOrderStatus.Paid, (await BillingTestData.LoadOrderAsync(factory, orderId)).Status);
    }

    [SqlFact]
    public async Task Pay07_DevPay_CancelledOrder_Returns409AndGrantsNothing()
    {
        // Chuẩn bị
        (HttpClient client, Guid vaultId, _) = await BillingTestData.CreateFreeVaultAsync(factory, 1);
        Guid orderId = await CreateXsOrderAsync(client);
        await client.PostAsync($"/api/v1/billing/orders/{orderId}/cancel", null);

        // Hành động
        HttpResponseMessage response = await DevPayAsync(client, orderId);

        // Kiểm tra
        JsonElement problem = await ApiTestHelpers.AssertProblemAsync(
            response, HttpStatusCode.Conflict, "PAYMENT_ORDER_NOT_PAYABLE");
        Assert.Equal("CANCELLED", problem.GetProperty("reason").GetString());
        VaultEntitlement entitlement = await LoadEntitlementAsync(vaultId);
        Assert.Equal(EntitlementStatus.Free, entitlement.Status);
        Assert.Equal(PaymentOrderStatus.Cancelled, (await BillingTestData.LoadOrderAsync(factory, orderId)).Status);
        Assert.Equal(0, await CountPaidLogsAsync(vaultId));
    }

    [SqlFact]
    public async Task Pay06_DevPay_OverdueOrder_Returns409AndGrantsNothing()
    {
        // Chuẩn bị: giờ trả sau hạn đơn
        (HttpClient client, Guid vaultId, _) = await BillingTestData.CreateFreeVaultAsync(factory, 1);
        Guid orderId = await CreateXsOrderAsync(client);
        await BillingTestData.MakeOrderOverdueAsync(factory, orderId);

        // Hành động
        HttpResponseMessage response = await DevPayAsync(client, orderId);

        // Kiểm tra
        JsonElement problem = await ApiTestHelpers.AssertProblemAsync(
            response, HttpStatusCode.Conflict, "PAYMENT_ORDER_NOT_PAYABLE");
        Assert.Equal("EXPIRED", problem.GetProperty("reason").GetString());
        Assert.Equal(EntitlementStatus.Free, (await LoadEntitlementAsync(vaultId)).Status);
        Assert.Equal(0, await CountPaidLogsAsync(vaultId));
    }

    [SqlFact]
    public async Task Pay06_DevPay_ExpiredStatusButPaidBeforeDeadline_IsStillGranted()
    {
        // Chuẩn bị: đơn bị đánh dấu Expired (D7) nhưng hạn đơn còn ở tương lai, tức tiền vào trước hạn (PAY-06)
        (HttpClient client, Guid vaultId, _) = await BillingTestData.CreateFreeVaultAsync(factory, 1);
        Guid orderId = await CreateXsOrderAsync(client);
        await BillingTestData.SetOrderStatusAsync(factory, orderId, PaymentOrderStatus.Expired);

        // Hành động
        HttpResponseMessage response = await DevPayAsync(client, orderId);

        // Kiểm tra
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("PAID", (await ApiTestHelpers.ReadJsonAsync(response)).GetProperty("status").GetString());
        Assert.Equal(EntitlementStatus.PaidActive, (await LoadEntitlementAsync(vaultId)).Status);
    }

    [SqlFact]
    public async Task Ac104_DevPay_ClosedVault_Returns409AndGrantsNothing()
    {
        // Chuẩn bị: kho đóng sau khi tạo đơn
        (HttpClient client, Guid vaultId, _) = await BillingTestData.CreateFreeVaultAsync(factory, 1);
        Guid orderId = await CreateXsOrderAsync(client);
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            await db.Vaults.Where(v => v.Id == vaultId)
                .ExecuteUpdateAsync(s => s.SetProperty(v => v.Status, VaultStatus.Closed));
        });

        // Hành động
        HttpResponseMessage response = await DevPayAsync(client, orderId);

        // Kiểm tra
        JsonElement problem = await ApiTestHelpers.AssertProblemAsync(
            response, HttpStatusCode.Conflict, "PAYMENT_ORDER_NOT_PAYABLE");
        Assert.Equal("VAULT_CLOSED", problem.GetProperty("reason").GetString());
        Assert.Equal(EntitlementStatus.Free, (await LoadEntitlementAsync(vaultId)).Status);
        Assert.Equal(PaymentOrderStatus.Pending, (await BillingTestData.LoadOrderAsync(factory, orderId)).Status);
    }

    [SqlFact]
    public async Task Addon03_DevPay_Addon_ServiceExpiryChanged_Returns409AndGrantsNothing()
    {
        // Chuẩn bị: sau khi tạo đơn, hạn kỳ bị đổi
        (HttpClient client, Guid vaultId, _) = await BillingTestData.CreateFreeVaultAsync(factory, null);
        await BillingTestData.MakePaidActiveAsync(factory, vaultId, TimeSpan.FromDays(180));
        Guid orderId = await CreateAddonOrderAsync(client, 1);
        await BillingTestData.MakePaidActiveAsync(factory, vaultId, TimeSpan.FromDays(400));

        // Hành động
        HttpResponseMessage response = await DevPayAsync(client, orderId);

        // Kiểm tra
        JsonElement problem = await ApiTestHelpers.AssertProblemAsync(
            response, HttpStatusCode.Conflict, "PAYMENT_ORDER_NOT_PAYABLE");
        Assert.Equal("ENTITLEMENT_CHANGED", problem.GetProperty("reason").GetString());
        Assert.Equal(0, (await LoadEntitlementAsync(vaultId)).ExtraBeneficiarySlots);
        Assert.Equal(PaymentOrderStatus.Pending, (await BillingTestData.LoadOrderAsync(factory, orderId)).Status);
        Assert.Equal(0, await CountPaidLogsAsync(vaultId));
    }

    [SqlFact]
    public async Task Addon03_DevPay_Addon_EntitlementBecameGrace_Returns409()
    {
        // Chuẩn bị
        (HttpClient client, Guid vaultId, _) = await BillingTestData.CreateFreeVaultAsync(factory, null);
        await BillingTestData.MakePaidActiveAsync(factory, vaultId, TimeSpan.FromDays(180));
        Guid orderId = await CreateAddonOrderAsync(client, 1);
        await ApiTestHelpers.SetEntitlementStatusAsync(factory, vaultId, EntitlementStatus.GraceReadOnly);

        // Hành động
        HttpResponseMessage response = await DevPayAsync(client, orderId);

        // Kiểm tra
        JsonElement problem = await ApiTestHelpers.AssertProblemAsync(
            response, HttpStatusCode.Conflict, "PAYMENT_ORDER_NOT_PAYABLE");
        Assert.Equal("ENTITLEMENT_CHANGED", problem.GetProperty("reason").GetString());
        Assert.Equal(0, (await LoadEntitlementAsync(vaultId)).ExtraBeneficiarySlots);
    }

    [SqlFact]
    public async Task D8_DevPay_Purchase_EntitlementNoLongerFree_Returns409()
    {
        // Chuẩn bị: kho được nâng lên XS bằng đường khác sau khi tạo đơn
        (HttpClient client, Guid vaultId, _) = await BillingTestData.CreateFreeVaultAsync(factory, 1);
        Guid orderId = await CreateXsOrderAsync(client);
        await ApiTestHelpers.UpgradeVaultToXsAsync(factory, vaultId);

        // Hành động
        HttpResponseMessage response = await DevPayAsync(client, orderId);

        // Kiểm tra
        JsonElement problem = await ApiTestHelpers.AssertProblemAsync(
            response, HttpStatusCode.Conflict, "PAYMENT_ORDER_NOT_PAYABLE");
        Assert.Equal("ENTITLEMENT_CHANGED", problem.GetProperty("reason").GetString());
        Assert.Equal(0, (await LoadEntitlementAsync(vaultId)).ExtraBeneficiarySlots);
    }

    [SqlFact]
    public async Task Sec83_DevPay_PlanLockedAfterOrderCreated_StillGrantsAndKeepsPlanStatus()
    {
        // Chuẩn bị: đơn tạo trước, sau đó kế hoạch vào chứng tử (§8.3: đơn tạo trước vẫn được cấp)
        (HttpClient client, Guid vaultId, _) = await BillingTestData.CreateFreeVaultAsync(factory, 1);
        Guid orderId = await CreateXsOrderAsync(client);
        await ApiTestHelpers.SetPlanStatusAsync(factory, vaultId, PlanStatus.DeathCaseOpen);

        // Hành động
        HttpResponseMessage response = await DevPayAsync(client, orderId);

        // Kiểm tra: cấp quyền nhưng không đổi kế hoạch, không gỡ chặn (PAY-09)
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(EntitlementStatus.PaidActive, (await LoadEntitlementAsync(vaultId)).Status);
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            HandoverPlan plan = await db.HandoverPlans.AsNoTracking().SingleAsync(p => p.VaultId == vaultId);
            Assert.Equal(PlanStatus.DeathCaseOpen, plan.Status);
        });
    }

    [SqlFact]
    public async Task Sec04_DevPay_OtherUsersOrderOrUnknownId_Returns404AndGrantsNothing()
    {
        // Chuẩn bị
        (HttpClient client, Guid vaultId, _) = await BillingTestData.CreateFreeVaultAsync(factory, 1);
        (HttpClient other, _, _) = await BillingTestData.CreateFreeVaultAsync(factory, 1);
        Guid orderId = await CreateXsOrderAsync(client);

        // Hành động
        HttpResponseMessage foreign = await DevPayAsync(other, orderId);
        HttpResponseMessage unknown = await DevPayAsync(client, Guid.NewGuid());

        // Kiểm tra
        await ApiTestHelpers.AssertProblemAsync(foreign, HttpStatusCode.NotFound, "PAYMENT_ORDER_NOT_FOUND");
        await ApiTestHelpers.AssertProblemAsync(unknown, HttpStatusCode.NotFound, "PAYMENT_ORDER_NOT_FOUND");
        Assert.Equal(EntitlementStatus.Free, (await LoadEntitlementAsync(vaultId)).Status);
        Assert.Equal(PaymentOrderStatus.Pending, (await BillingTestData.LoadOrderAsync(factory, orderId)).Status);
    }

    [SqlFact]
    public async Task Sec04_DevPay_WithoutLogin_Returns401()
    {
        // Chuẩn bị
        HttpClient anonymous = factory.CreateClient();

        // Hành động
        HttpResponseMessage response = await DevPayAsync(anonymous, Guid.NewGuid());

        // Kiểm tra
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [SqlFact]
    public async Task D9_DevPay_SwitchOff_RouteNotMappedAndNothingGranted()
    {
        // Chuẩn bị: cùng database, nhưng công tắc dev-pay tắt
        (HttpClient client, Guid vaultId, _) = await BillingTestData.CreateFreeVaultAsync(factory, 1);
        Guid orderId = await CreateXsOrderAsync(client);
        using (WebApplicationFactory<Program> switchedOff = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((context, configuration) =>
            {
                Dictionary<string, string?> settings = new Dictionary<string, string?>();
                settings["Billing:DevPaymentEnabled"] = "false";
                configuration.AddInMemoryCollection(settings);
            });
        }))
        {
            HttpClient switchedOffClient = switchedOff.CreateClient();
            switchedOffClient.DefaultRequestHeaders.Authorization = client.DefaultRequestHeaders.Authorization;

            // Hành động
            HttpResponseMessage response = await DevPayAsync(switchedOffClient, orderId);

            // Kiểm tra: route không tồn tại
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }
        Assert.Equal(EntitlementStatus.Free, (await LoadEntitlementAsync(vaultId)).Status);
        Assert.Equal(PaymentOrderStatus.Pending, (await BillingTestData.LoadOrderAsync(factory, orderId)).Status);
        Assert.Equal(0, await CountPaidLogsAsync(vaultId));
    }
}
