using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;
using LegacyVault.IntegrationTests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace LegacyVault.IntegrationTests.Payment;

/// <summary>Tạo, xem, đơn hiện tại và hủy đơn thanh toán trên API thật và SQL Server tạm (SRS §8.3, PAY-04, PAY-11).</summary>
public sealed class PaymentOrderFlowTests : IAsyncLifetime
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

    private static Task<HttpResponseMessage> CreateOrderAsync(HttpClient client, object body)
    {
        return client.PostAsJsonAsync("/api/v1/billing/orders", body);
    }

    private static object XsThreeYearsOneSlot()
    {
        return new { tier = "XS", years = 3, extraSlots = 1 };
    }

    /// <summary>Tạo đơn PURCHASE XS 3 năm + 1 suất (201) và trả JSON đơn.</summary>
    private static async Task<JsonElement> CreateXsOrderAsync(HttpClient client)
    {
        HttpResponseMessage response = await CreateOrderAsync(client, XsThreeYearsOneSlot());
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await ApiTestHelpers.ReadJsonAsync(response);
    }

    [SqlFact]
    public async Task Pay11_CreateOrder_Purchase_Returns201WithLockedPriceAndPaymentInfo()
    {
        // Chuẩn bị
        (HttpClient client, Guid vaultId, Guid ownerId) = await BillingTestData.CreateFreeVaultAsync(factory, 1);
        DateTimeOffset before = DateTimeOffset.UtcNow;

        // Hành động: body cố ý kèm số tiền, loại đơn và trạng thái giả (AC-94)
        HttpResponseMessage response = await CreateOrderAsync(client,
            new { tier = "XS", years = 3, extraSlots = 1, totalAmount = 1, kind = "ADDON", status = "PAID" });

        // Kiểm tra
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        JsonElement order = await ApiTestHelpers.ReadJsonAsync(response);
        Assert.Equal("PURCHASE", order.GetProperty("kind").GetString());
        Assert.Equal("PENDING", order.GetProperty("status").GetString());
        Assert.Equal("XS", order.GetProperty("tier").GetString());
        Assert.Equal(3, order.GetProperty("years").GetInt32());
        Assert.Equal(1, order.GetProperty("extraSlots").GetInt32());
        Assert.Equal(499000, order.GetProperty("tierAmount").GetInt64());
        Assert.Equal(147000, order.GetProperty("slotAmount").GetInt64());
        Assert.Equal(646000, order.GetProperty("totalAmount").GetInt64());
        Assert.Equal("VND", order.GetProperty("currency").GetString());
        Assert.Equal(200 * 1048576L, order.GetProperty("storageQuotaBytes").GetInt64());
        Assert.Equal(2, order.GetProperty("includedBeneficiarySlots").GetInt32());

        string paymentCode = order.GetProperty("paymentCode").GetString()!;
        Assert.Matches("^LV[ABCDEFGHJKLMNPQRSTUVWXYZ23456789]{8}$", paymentCode);
        Assert.Equal(paymentCode, order.GetProperty("transferContent").GetString());
        Assert.Equal("0000000000", order.GetProperty("bank").GetProperty("accountNumber").GetString());
        string qr = order.GetProperty("qrImageUrl").GetString()!;
        Assert.StartsWith("https://qr.sepay.vn/img?", qr);
        Assert.Contains("amount=646000", qr);
        Assert.Contains("des=" + paymentCode, qr);

        DateTimeOffset expiresAt = order.GetProperty("expiresAt").GetDateTimeOffset();
        Assert.InRange(expiresAt, before.AddMinutes(15).AddSeconds(-2), DateTimeOffset.UtcNow.AddMinutes(15).AddSeconds(2));
        Assert.Equal("Thanh toán không tự kích hoạt bàn giao.", order.GetProperty("notice").GetString());
        Assert.NotEqual(JsonValueKind.Null, order.GetProperty("serverTime").ValueKind);

        // Không lộ dữ liệu nội bộ
        Assert.False(order.TryGetProperty("ownerId", out _));
        Assert.False(order.TryGetProperty("vaultId", out _));
        Assert.False(order.TryGetProperty("rowVersion", out _));

        // DB: giá và chính sách được chốt, nhật ký có dòng tạo đơn (SEC-13)
        Guid orderId = order.GetProperty("id").GetGuid();
        PaymentOrder stored = await BillingTestData.LoadOrderAsync(factory, orderId);
        Assert.Equal(vaultId, stored.VaultId);
        Assert.Equal(ownerId, stored.OwnerId);
        Assert.Equal("2026-10", stored.PolicyVersion);
        Assert.Equal(PaymentOrderStatus.Pending, stored.Status);
        Assert.Equal(646000, stored.TotalAmount);
        Assert.Null(stored.BaseServiceExpiresAt);
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            List<AuditLog> logs = await db.AuditLogs
                .Where(l => l.VaultId == vaultId && l.Action == AuditAction.PaymentOrderCreated).ToListAsync();
            Assert.Single(logs);
            Assert.Equal(orderId, logs[0].TargetId);
            Assert.Equal(AuditTargetType.PaymentOrder, logs[0].TargetType);
            Assert.Equal(ownerId, logs[0].ActorUserId);
        });
    }

    [SqlFact]
    public async Task Pay04_CreateOrder_SameSelectionAgain_Returns200WithSameOrder()
    {
        // Chuẩn bị
        (HttpClient client, Guid vaultId, _) = await BillingTestData.CreateFreeVaultAsync(factory, 1);
        JsonElement first = await CreateXsOrderAsync(client);

        // Hành động
        HttpResponseMessage second = await CreateOrderAsync(client, XsThreeYearsOneSlot());

        // Kiểm tra
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        JsonElement again = await ApiTestHelpers.ReadJsonAsync(second);
        Assert.Equal(first.GetProperty("id").GetGuid(), again.GetProperty("id").GetGuid());
        Assert.Equal(first.GetProperty("paymentCode").GetString(), again.GetProperty("paymentCode").GetString());
        Assert.Equal(1, await BillingTestData.CountOrdersAsync(factory, vaultId, null));
    }

    [SqlFact]
    public async Task Pay04_CreateOrder_DifferentSelection_Returns409WithPendingOrderId()
    {
        // Chuẩn bị
        (HttpClient client, Guid vaultId, _) = await BillingTestData.CreateFreeVaultAsync(factory, 1);
        JsonElement first = await CreateXsOrderAsync(client);

        // Hành động
        HttpResponseMessage second = await CreateOrderAsync(client, new { tier = "XS_MAX", years = 1, extraSlots = 0 });

        // Kiểm tra
        JsonElement problem = await ApiTestHelpers.AssertProblemAsync(second, HttpStatusCode.Conflict, "PAYMENT_ORDER_PENDING");
        Assert.Equal(first.GetProperty("id").GetGuid(), problem.GetProperty("orderId").GetGuid());
        Assert.Equal(1, await BillingTestData.CountOrdersAsync(factory, vaultId, null));
    }

    [SqlFact]
    public async Task Pay04_D7_OverduePendingOrder_ReplacedByNewOrderInOneSave()
    {
        // Chuẩn bị: đơn cũ quá 15 phút, chưa có job nên vẫn đang Pending trong DB
        (HttpClient client, Guid vaultId, _) = await BillingTestData.CreateFreeVaultAsync(factory, 1);
        JsonElement oldOrder = await CreateXsOrderAsync(client);
        Guid oldId = oldOrder.GetProperty("id").GetGuid();
        await BillingTestData.MakeOrderOverdueAsync(factory, oldId);

        // Hành động: cùng lựa chọn nhưng đơn cũ đã hết hạn nên phải tạo đơn mới
        HttpResponseMessage response = await CreateOrderAsync(client, XsThreeYearsOneSlot());

        // Kiểm tra: đơn cũ EXPIRED, đúng một đơn PENDING mới, không vướng unique có lọc
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        JsonElement newOrder = await ApiTestHelpers.ReadJsonAsync(response);
        Assert.NotEqual(oldId, newOrder.GetProperty("id").GetGuid());
        Assert.NotEqual(oldOrder.GetProperty("paymentCode").GetString(), newOrder.GetProperty("paymentCode").GetString());
        PaymentOrder storedOld = await BillingTestData.LoadOrderAsync(factory, oldId);
        Assert.Equal(PaymentOrderStatus.Expired, storedOld.Status);
        Assert.Equal(1, await BillingTestData.CountOrdersAsync(factory, vaultId, PaymentOrderStatus.Pending));
        Assert.Equal(1, await BillingTestData.CountOrdersAsync(factory, vaultId, PaymentOrderStatus.Expired));
    }

    [SqlFact]
    public async Task Pay04_D7_OverduePendingOrder_DifferentSelection_AlsoReplaced()
    {
        // Chuẩn bị
        (HttpClient client, Guid vaultId, _) = await BillingTestData.CreateFreeVaultAsync(factory, 1);
        JsonElement oldOrder = await CreateXsOrderAsync(client);
        await BillingTestData.MakeOrderOverdueAsync(factory, oldOrder.GetProperty("id").GetGuid());

        // Hành động: đơn cũ đã hết hạn thì không còn chặn lựa chọn khác
        HttpResponseMessage response = await CreateOrderAsync(client, new { tier = "XS_MAX", years = 1, extraSlots = 0 });

        // Kiểm tra
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        JsonElement newOrder = await ApiTestHelpers.ReadJsonAsync(response);
        Assert.Equal("XS_MAX", newOrder.GetProperty("tier").GetString());
        Assert.Equal(1, await BillingTestData.CountOrdersAsync(factory, vaultId, PaymentOrderStatus.Pending));
    }

    [SqlFact]
    public async Task Pay04_CreateOrder_ConcurrentSameSelection_OneOrderOnly()
    {
        // Chuẩn bị
        (HttpClient client, Guid vaultId, _) = await BillingTestData.CreateFreeVaultAsync(factory, 1);

        // Hành động: sáu request tạo đơn cùng lúc
        List<Task<HttpResponseMessage>> posts = new List<Task<HttpResponseMessage>>();
        for (int i = 0; i < 6; i++)
        {
            posts.Add(CreateOrderAsync(client, XsThreeYearsOneSlot()));
        }
        HttpResponseMessage[] responses = await Task.WhenAll(posts);

        // Kiểm tra: đúng một 201, còn lại 200 cùng id; DB chỉ có một đơn
        int created = 0;
        HashSet<Guid> ids = new HashSet<Guid>();
        foreach (HttpResponseMessage response in responses)
        {
            Assert.True(response.StatusCode == HttpStatusCode.Created || response.StatusCode == HttpStatusCode.OK,
                await response.Content.ReadAsStringAsync());
            if (response.StatusCode == HttpStatusCode.Created)
            {
                created++;
            }
            ids.Add((await ApiTestHelpers.ReadJsonAsync(response)).GetProperty("id").GetGuid());
        }
        Assert.Equal(1, created);
        Assert.Single(ids);
        Assert.Equal(1, await BillingTestData.CountOrdersAsync(factory, vaultId, null));
    }

    [SqlFact]
    public async Task Pay04_CreateOrder_ConcurrentDifferentSelections_OneWinner()
    {
        // Chuẩn bị
        (HttpClient client, Guid vaultId, _) = await BillingTestData.CreateFreeVaultAsync(factory, 1);

        // Hành động: hai lựa chọn khác nhau, mỗi lựa chọn ba request cùng lúc
        List<Task<HttpResponseMessage>> posts = new List<Task<HttpResponseMessage>>();
        for (int i = 0; i < 3; i++)
        {
            posts.Add(CreateOrderAsync(client, new { tier = "XS", years = 1, extraSlots = 0 }));
            posts.Add(CreateOrderAsync(client, new { tier = "XS_MAX", years = 1, extraSlots = 0 }));
        }
        HttpResponseMessage[] responses = await Task.WhenAll(posts);

        // Kiểm tra: đúng một đơn được tạo; bên khác lựa chọn nhận 409, bên cùng lựa chọn nhận 200
        int created = 0;
        foreach (HttpResponseMessage response in responses)
        {
            if (response.StatusCode == HttpStatusCode.Created)
            {
                created++;
            }
            else if (response.StatusCode == HttpStatusCode.Conflict)
            {
                await ApiTestHelpers.AssertProblemAsync(response, HttpStatusCode.Conflict, "PAYMENT_ORDER_PENDING");
            }
            else
            {
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            }
        }
        Assert.Equal(1, created);
        Assert.Equal(1, await BillingTestData.CountOrdersAsync(factory, vaultId, null));
        Assert.Equal(1, await BillingTestData.CountOrdersAsync(factory, vaultId, PaymentOrderStatus.Pending));
    }

    [SqlFact]
    public async Task Sec83_CreateOrder_WithoutOwnerEkyc_Returns409AndWritesNothing()
    {
        // Chuẩn bị
        (HttpClient client, Guid vaultId, _) = await BillingTestData.CreateFreeVaultAsync(factory, null);

        // Hành động
        HttpResponseMessage response = await CreateOrderAsync(client, XsThreeYearsOneSlot());

        // Kiểm tra
        JsonElement problem = await ApiTestHelpers.AssertProblemAsync(response, HttpStatusCode.Conflict, "PAYMENT_NOT_ALLOWED");
        Assert.Equal("OWNER_EKYC_REQUIRED", problem.GetProperty("reason").GetString());
        Assert.Equal(0, await BillingTestData.CountOrdersAsync(factory, vaultId, null));
    }

    [SqlFact]
    public async Task Sec83_CreateOrder_PlanLockedOrInvalidInput_Rejected()
    {
        // Chuẩn bị
        (HttpClient lockedClient, Guid lockedVaultId, _) = await BillingTestData.CreateFreeVaultAsync(factory, 1);
        await ApiTestHelpers.SetPlanStatusAsync(factory, lockedVaultId, PlanStatus.DeathCaseOpen);
        (HttpClient client, Guid vaultId, _) = await BillingTestData.CreateFreeVaultAsync(factory, 1);

        // Hành động
        HttpResponseMessage locked = await CreateOrderAsync(lockedClient, XsThreeYearsOneSlot());
        HttpResponseMessage invalid = await CreateOrderAsync(client, new { tier = "XS", years = 2, extraSlots = 0 });

        // Kiểm tra
        JsonElement problem = await ApiTestHelpers.AssertProblemAsync(locked, HttpStatusCode.Conflict, "PAYMENT_NOT_ALLOWED");
        Assert.Equal("PLAN_LOCKED", problem.GetProperty("reason").GetString());
        await ApiTestHelpers.AssertProblemAsync(invalid, HttpStatusCode.BadRequest, "VALIDATION_ERROR");
        Assert.Equal(0, await BillingTestData.CountOrdersAsync(factory, lockedVaultId, null));
        Assert.Equal(0, await BillingTestData.CountOrdersAsync(factory, vaultId, null));
    }

    [SqlFact]
    public async Task Addon02_Addon03_CreateOrder_AddonWith180DaysLeft_Is24165AndExpiresInFifteenMinutes()
    {
        // Chuẩn bị: XS còn 180 ngày
        (HttpClient client, Guid vaultId, _) = await BillingTestData.CreateFreeVaultAsync(factory, null);
        await BillingTestData.MakePaidActiveAsync(factory, vaultId, TimeSpan.FromDays(180));
        DateTimeOffset before = DateTimeOffset.UtcNow;

        // Hành động
        HttpResponseMessage response = await CreateOrderAsync(client, new { extraSlots = 1 });

        // Kiểm tra
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        JsonElement order = await ApiTestHelpers.ReadJsonAsync(response);
        Assert.Equal("ADDON", order.GetProperty("kind").GetString());
        Assert.Equal("XS", order.GetProperty("tier").GetString());
        Assert.Equal(JsonValueKind.Null, order.GetProperty("years").ValueKind);
        Assert.Equal(0, order.GetProperty("tierAmount").GetInt64());
        Assert.Equal(24165, order.GetProperty("totalAmount").GetInt64());
        Assert.InRange(order.GetProperty("expiresAt").GetDateTimeOffset(),
            before.AddMinutes(15).AddSeconds(-2), DateTimeOffset.UtcNow.AddMinutes(15).AddSeconds(2));
        PaymentOrder stored = await BillingTestData.LoadOrderAsync(factory, order.GetProperty("id").GetGuid());
        Assert.NotNull(stored.BaseServiceExpiresAt);
        Assert.Equal(PaymentOrderKind.Addon, stored.Kind);
    }

    [SqlFact]
    public async Task Addon03_CreateOrder_AddonServiceEndsInFiveMinutes_OrderExpiresAtServiceEnd()
    {
        // Chuẩn bị: kỳ hết sau 5 phút, sớm hơn mốc 15 phút
        (HttpClient client, Guid vaultId, _) = await BillingTestData.CreateFreeVaultAsync(factory, null);
        await BillingTestData.MakePaidActiveAsync(factory, vaultId, TimeSpan.FromMinutes(5));

        // Hành động
        HttpResponseMessage response = await CreateOrderAsync(client, new { extraSlots = 2 });

        // Kiểm tra: hạn đơn bằng đúng hạn của kỳ
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        JsonElement order = await ApiTestHelpers.ReadJsonAsync(response);
        PaymentOrder stored = await BillingTestData.LoadOrderAsync(factory, order.GetProperty("id").GetGuid());
        Assert.NotNull(stored.BaseServiceExpiresAt);
        Assert.Equal(stored.BaseServiceExpiresAt!.Value.UtcTicks / 10, stored.ExpiresAt.UtcTicks / 10);
        Assert.True(stored.ExpiresAt < DateTimeOffset.UtcNow.AddMinutes(6));
    }

    [SqlFact]
    public async Task Sec83_GetCurrentOrder_NoOrder_Returns204_ThenReturnsPendingOrder()
    {
        // Chuẩn bị
        (HttpClient client, _, _) = await BillingTestData.CreateFreeVaultAsync(factory, 1);

        // Hành động / Kiểm tra: chưa có đơn
        HttpResponseMessage none = await client.GetAsync("/api/v1/billing/orders/current");
        Assert.Equal(HttpStatusCode.NoContent, none.StatusCode);

        // Hành động / Kiểm tra: có đơn
        JsonElement created = await CreateXsOrderAsync(client);
        HttpResponseMessage current = await client.GetAsync("/api/v1/billing/orders/current");
        Assert.Equal(HttpStatusCode.OK, current.StatusCode);
        JsonElement order = await ApiTestHelpers.ReadJsonAsync(current);
        Assert.Equal(created.GetProperty("id").GetGuid(), order.GetProperty("id").GetGuid());
        Assert.Equal("PENDING", order.GetProperty("status").GetString());
    }

    [SqlFact]
    public async Task Sec83_GetCurrentOrder_OverdueOrder_Returns204AndMarksExpired()
    {
        // Chuẩn bị
        (HttpClient client, Guid vaultId, _) = await BillingTestData.CreateFreeVaultAsync(factory, 1);
        JsonElement created = await CreateXsOrderAsync(client);
        Guid orderId = created.GetProperty("id").GetGuid();
        await BillingTestData.MakeOrderOverdueAsync(factory, orderId);

        // Hành động
        HttpResponseMessage current = await client.GetAsync("/api/v1/billing/orders/current");

        // Kiểm tra: D7 đổi Expired ngay trong lần đọc
        Assert.Equal(HttpStatusCode.NoContent, current.StatusCode);
        PaymentOrder stored = await BillingTestData.LoadOrderAsync(factory, orderId);
        Assert.Equal(PaymentOrderStatus.Expired, stored.Status);
        Assert.Equal(0, await BillingTestData.CountOrdersAsync(factory, vaultId, PaymentOrderStatus.Pending));
    }

    [SqlFact]
    public async Task Sec83_GetCurrentOrder_UserWithoutVault_Returns404()
    {
        // Chuẩn bị
        HttpClient client = await ApiTestHelpers.CreateClientForNewUserAsync(factory, emailVerified: true);

        // Hành động
        HttpResponseMessage response = await client.GetAsync("/api/v1/billing/orders/current");

        // Kiểm tra
        await ApiTestHelpers.AssertProblemAsync(response, HttpStatusCode.NotFound, "VAULT_NOT_FOUND");
    }

    [SqlFact]
    public async Task Sec04_GetOrder_OwnOrderOk_OtherUserOrUnknownIdGets404()
    {
        // Chuẩn bị
        (HttpClient client, _, _) = await BillingTestData.CreateFreeVaultAsync(factory, 1);
        (HttpClient other, _, _) = await BillingTestData.CreateFreeVaultAsync(factory, 1);
        JsonElement created = await CreateXsOrderAsync(client);
        Guid orderId = created.GetProperty("id").GetGuid();

        // Hành động
        HttpResponseMessage own = await client.GetAsync($"/api/v1/billing/orders/{orderId}");
        HttpResponseMessage foreign = await other.GetAsync($"/api/v1/billing/orders/{orderId}");
        HttpResponseMessage unknown = await client.GetAsync($"/api/v1/billing/orders/{Guid.NewGuid()}");

        // Kiểm tra: đơn của người khác cũng 404 như đơn không tồn tại
        Assert.Equal(HttpStatusCode.OK, own.StatusCode);
        Assert.Equal(orderId, (await ApiTestHelpers.ReadJsonAsync(own)).GetProperty("id").GetGuid());
        await ApiTestHelpers.AssertProblemAsync(foreign, HttpStatusCode.NotFound, "PAYMENT_ORDER_NOT_FOUND");
        await ApiTestHelpers.AssertProblemAsync(unknown, HttpStatusCode.NotFound, "PAYMENT_ORDER_NOT_FOUND");
    }

    [SqlFact]
    public async Task Pay12_GetOrder_OverdueOrder_ReturnsExpiredAndSavesIt()
    {
        // Chuẩn bị
        (HttpClient client, _, _) = await BillingTestData.CreateFreeVaultAsync(factory, 1);
        JsonElement created = await CreateXsOrderAsync(client);
        Guid orderId = created.GetProperty("id").GetGuid();
        await BillingTestData.MakeOrderOverdueAsync(factory, orderId);

        // Hành động
        HttpResponseMessage response = await client.GetAsync($"/api/v1/billing/orders/{orderId}");

        // Kiểm tra: không còn QR cho đơn đã hết hạn
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        JsonElement order = await ApiTestHelpers.ReadJsonAsync(response);
        Assert.Equal("EXPIRED", order.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, order.GetProperty("qrImageUrl").ValueKind);
        PaymentOrder stored = await BillingTestData.LoadOrderAsync(factory, orderId);
        Assert.Equal(PaymentOrderStatus.Expired, stored.Status);
    }

    [SqlFact]
    public async Task Pay12_GetOrder_ConcurrentReadsOfOverdueOrder_NeverFail()
    {
        // Chuẩn bị: nhiều lần đọc cùng lúc đều thấy đơn quá hạn và cùng đánh dấu Expired
        (HttpClient client, _, _) = await BillingTestData.CreateFreeVaultAsync(factory, 1);
        JsonElement created = await CreateXsOrderAsync(client);
        Guid orderId = created.GetProperty("id").GetGuid();
        await BillingTestData.MakeOrderOverdueAsync(factory, orderId);

        // Hành động
        List<Task<HttpResponseMessage>> gets = new List<Task<HttpResponseMessage>>();
        for (int i = 0; i < 6; i++)
        {
            gets.Add(client.GetAsync($"/api/v1/billing/orders/{orderId}"));
        }
        HttpResponseMessage[] responses = await Task.WhenAll(gets);

        // Kiểm tra: không request nào nhận 409 EDIT_CONFLICT, vì bên thua đọc lại trạng thái mới
        foreach (HttpResponseMessage response in responses)
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            JsonElement order = await ApiTestHelpers.ReadJsonAsync(response);
            Assert.Equal("EXPIRED", order.GetProperty("status").GetString());
        }
    }

    [SqlFact]
    public async Task Pay04_CancelOrder_CancelsLogsAndAllowsNewOrder()
    {
        // Chuẩn bị
        (HttpClient client, Guid vaultId, Guid ownerId) = await BillingTestData.CreateFreeVaultAsync(factory, 1);
        JsonElement created = await CreateXsOrderAsync(client);
        Guid orderId = created.GetProperty("id").GetGuid();

        // Hành động
        HttpResponseMessage cancel = await client.PostAsync($"/api/v1/billing/orders/{orderId}/cancel", null);

        // Kiểm tra
        Assert.Equal(HttpStatusCode.OK, cancel.StatusCode);
        JsonElement cancelled = await ApiTestHelpers.ReadJsonAsync(cancel);
        Assert.Equal("CANCELLED", cancelled.GetProperty("status").GetString());
        Assert.NotEqual(JsonValueKind.Null, cancelled.GetProperty("cancelledAt").ValueKind);
        Assert.Equal(JsonValueKind.Null, cancelled.GetProperty("qrImageUrl").ValueKind);
        PaymentOrder stored = await BillingTestData.LoadOrderAsync(factory, orderId);
        Assert.Equal(PaymentOrderStatus.Cancelled, stored.Status);
        Assert.NotNull(stored.CancelledAt);
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            List<AuditLog> logs = await db.AuditLogs
                .Where(l => l.VaultId == vaultId && l.Action == AuditAction.PaymentOrderCancelled).ToListAsync();
            Assert.Single(logs);
            Assert.Equal(orderId, logs[0].TargetId);
            Assert.Equal(ownerId, logs[0].ActorUserId);
        });

        // Sau khi hủy thì tạo được đơn mới (cùng lựa chọn vẫn ra đơn khác)
        HttpResponseMessage next = await CreateOrderAsync(client, XsThreeYearsOneSlot());
        Assert.Equal(HttpStatusCode.Created, next.StatusCode);
        Assert.NotEqual(orderId, (await ApiTestHelpers.ReadJsonAsync(next)).GetProperty("id").GetGuid());
    }

    [SqlFact]
    public async Task Pay04_CancelOrder_Repeated_ReturnsCancelledWithoutError_AndLogsOnce()
    {
        // Chuẩn bị
        (HttpClient client, Guid vaultId, _) = await BillingTestData.CreateFreeVaultAsync(factory, 1);
        JsonElement created = await CreateXsOrderAsync(client);
        Guid orderId = created.GetProperty("id").GetGuid();
        await client.PostAsync($"/api/v1/billing/orders/{orderId}/cancel", null);

        // Hành động
        HttpResponseMessage again = await client.PostAsync($"/api/v1/billing/orders/{orderId}/cancel", null);

        // Kiểm tra
        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
        Assert.Equal("CANCELLED", (await ApiTestHelpers.ReadJsonAsync(again)).GetProperty("status").GetString());
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            int logs = await db.AuditLogs.CountAsync(l => l.VaultId == vaultId && l.Action == AuditAction.PaymentOrderCancelled);
            Assert.Equal(1, logs);
        });
    }

    [SqlFact]
    public async Task Pay04_CancelOrder_PaidOrder_Returns409AndKeepsPaid()
    {
        // Chuẩn bị: đơn đã được thanh toán
        (HttpClient client, _, _) = await BillingTestData.CreateFreeVaultAsync(factory, 1);
        JsonElement created = await CreateXsOrderAsync(client);
        Guid orderId = created.GetProperty("id").GetGuid();
        await BillingTestData.SetOrderStatusAsync(factory, orderId, PaymentOrderStatus.Paid);

        // Hành động
        HttpResponseMessage response = await client.PostAsync($"/api/v1/billing/orders/{orderId}/cancel", null);

        // Kiểm tra
        JsonElement problem = await ApiTestHelpers.AssertProblemAsync(
            response, HttpStatusCode.Conflict, "PAYMENT_ORDER_NOT_PAYABLE");
        Assert.Equal("PAID", problem.GetProperty("reason").GetString());
        Assert.Equal(PaymentOrderStatus.Paid, (await BillingTestData.LoadOrderAsync(factory, orderId)).Status);
    }

    [SqlFact]
    public async Task Pay04_CancelOrder_OverdueOrder_Returns409ExpiredAndMarksExpired()
    {
        // Chuẩn bị
        (HttpClient client, _, _) = await BillingTestData.CreateFreeVaultAsync(factory, 1);
        JsonElement created = await CreateXsOrderAsync(client);
        Guid orderId = created.GetProperty("id").GetGuid();
        await BillingTestData.MakeOrderOverdueAsync(factory, orderId);

        // Hành động
        HttpResponseMessage response = await client.PostAsync($"/api/v1/billing/orders/{orderId}/cancel", null);

        // Kiểm tra
        JsonElement problem = await ApiTestHelpers.AssertProblemAsync(
            response, HttpStatusCode.Conflict, "PAYMENT_ORDER_NOT_PAYABLE");
        Assert.Equal("EXPIRED", problem.GetProperty("reason").GetString());
        Assert.Equal(PaymentOrderStatus.Expired, (await BillingTestData.LoadOrderAsync(factory, orderId)).Status);
    }

    [SqlFact]
    public async Task Sec04_CancelOrder_OtherUsersOrder_Returns404AndOrderUnchanged()
    {
        // Chuẩn bị
        (HttpClient client, _, _) = await BillingTestData.CreateFreeVaultAsync(factory, 1);
        (HttpClient other, _, _) = await BillingTestData.CreateFreeVaultAsync(factory, 1);
        JsonElement created = await CreateXsOrderAsync(client);
        Guid orderId = created.GetProperty("id").GetGuid();

        // Hành động
        HttpResponseMessage response = await other.PostAsync($"/api/v1/billing/orders/{orderId}/cancel", null);

        // Kiểm tra
        await ApiTestHelpers.AssertProblemAsync(response, HttpStatusCode.NotFound, "PAYMENT_ORDER_NOT_FOUND");
        Assert.Equal(PaymentOrderStatus.Pending, (await BillingTestData.LoadOrderAsync(factory, orderId)).Status);
    }

    [SqlFact]
    public async Task Sec04_OrderEndpoints_WithoutLogin_Return401()
    {
        // Chuẩn bị
        HttpClient anonymous = factory.CreateClient();

        // Hành động
        HttpResponseMessage create = await CreateOrderAsync(anonymous, XsThreeYearsOneSlot());
        HttpResponseMessage current = await anonymous.GetAsync("/api/v1/billing/orders/current");
        HttpResponseMessage get = await anonymous.GetAsync($"/api/v1/billing/orders/{Guid.NewGuid()}");
        HttpResponseMessage cancel = await anonymous.PostAsync($"/api/v1/billing/orders/{Guid.NewGuid()}/cancel", null);

        // Kiểm tra
        Assert.Equal(HttpStatusCode.Unauthorized, create.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, current.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, get.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, cancel.StatusCode);
    }
}
