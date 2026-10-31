using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LegacyVault.Application.Services;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;
using LegacyVault.IntegrationTests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace LegacyVault.IntegrationTests.Payment;

/// <summary>
/// Webhook SePay bước đối chiếu và cấp quyền (SRS §8.3 bước 6–9, PAY-02..07, AC-28, AC-29, AC-30, AC-90, AC-97, AC-104):
/// khớp đơn thì cấp quyền đúng một lần; lệch thì giao dịch ReconciliationRequired, không cấp, mở sự cố PAYMENT.
/// Chạy API thật trên SQL Server database tạm; số tài khoản lấy từ cấu hình của chính factory.
/// </summary>
public sealed class SePayWebhookReconcileTests : IAsyncLifetime
{
    private const string WebhookUrl = "/api/v1/billing/sepay/webhook";

    private ApiFactory factory = null!;
    private long nextTransactionId = 100000;

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

    /// <summary>Đơn vừa tạo qua API: Id, mã thanh toán và tổng tiền.</summary>
    private sealed class CreatedOrder
    {
        public Guid OrderId { get; set; }

        public string PaymentCode { get; set; } = string.Empty;

        public long TotalAmount { get; set; }
    }

    private string BankAccountNumber()
    {
        using (IServiceScope scope = factory.Services.CreateScope())
        {
            IBillingBankSettings bank = scope.ServiceProvider.GetRequiredService<IBillingBankSettings>();
            return bank.AccountNumber;
        }
    }

    private static async Task<CreatedOrder> ReadCreatedOrderAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        JsonElement json = await ApiTestHelpers.ReadJsonAsync(response);
        CreatedOrder order = new CreatedOrder();
        order.OrderId = json.GetProperty("id").GetGuid();
        order.PaymentCode = json.GetProperty("paymentCode").GetString()!;
        order.TotalAmount = json.GetProperty("totalAmount").GetInt64();
        return order;
    }

    /// <summary>Đơn mua mới XS 1 năm, 0 suất thêm (199.000đ).</summary>
    private static async Task<CreatedOrder> CreatePurchaseOrderAsync(HttpClient client)
    {
        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/v1/billing/orders", new { tier = "XS", years = 1, extraSlots = 0 });
        return await ReadCreatedOrderAsync(response);
    }

    private static async Task<CreatedOrder> CreateAddonOrderAsync(HttpClient client, int extraSlots)
    {
        HttpResponseMessage response = await client.PostAsJsonAsync("/api/v1/billing/orders", new { extraSlots });
        return await ReadCreatedOrderAsync(response);
    }

    /// <summary>Giờ Việt Nam dạng SePay gửi: yyyy-MM-dd HH:mm:ss.</summary>
    private static string VietnamTimeText(DateTimeOffset moment)
    {
        DateTimeOffset vietnam = moment.ToOffset(TimeSpan.FromHours(7));
        return vietnam.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
    }

    private Dictionary<string, object?> Payload(long id, string content, long amount, DateTimeOffset at)
    {
        Dictionary<string, object?> payload = new Dictionary<string, object?>();
        payload["id"] = id;
        payload["gateway"] = "MBBank";
        payload["transactionDate"] = VietnamTimeText(at);
        payload["accountNumber"] = BankAccountNumber();
        payload["code"] = null;
        payload["content"] = content;
        payload["transferType"] = "in";
        payload["transferAmount"] = amount;
        payload["referenceCode"] = "FT26289000001";
        return payload;
    }

    /// <summary>Payload khớp đơn: đúng mã trong nội dung, đúng số tiền, tiền vào lúc này.</summary>
    private Dictionary<string, object?> PayloadFor(CreatedOrder order, long id)
    {
        return Payload(id, "chuyen tien " + order.PaymentCode, order.TotalAmount, DateTimeOffset.UtcNow);
    }

    private long NewTransactionId()
    {
        nextTransactionId++;
        return nextTransactionId;
    }

    private async Task<HttpResponseMessage> SendAsync(Dictionary<string, object?> payload)
    {
        HttpClient client = factory.CreateClient();
        HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, WebhookUrl);
        request.Content = JsonContent.Create(payload);
        request.Headers.TryAddWithoutValidation("Authorization", "Apikey " + ApiFactory.SePayTestApiKey);
        return await client.SendAsync(request);
    }

    private async Task<PaymentTransaction> LoadTransactionAsync(long providerTransactionId)
    {
        PaymentTransaction? loaded = null;
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            loaded = await db.PaymentTransactions.AsNoTracking()
                .SingleAsync(t => t.ProviderTransactionId == providerTransactionId);
        });
        return loaded!;
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

    private async Task<List<Incident>> LoadPaymentIncidentsAsync()
    {
        List<Incident> incidents = new List<Incident>();
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            incidents = await db.Set<Incident>().AsNoTracking()
                .Where(i => i.Type == IncidentType.Payment).ToListAsync();
        });
        return incidents;
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

    /// <summary>Gửi webhook, kiểm 200 success rồi trả giao dịch đã lưu.</summary>
    private async Task<PaymentTransaction> SendAndLoadAsync(Dictionary<string, object?> payload)
    {
        long id = (long)payload["id"]!;
        HttpResponseMessage response = await SendAsync(payload);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("{\"success\":true}", await response.Content.ReadAsStringAsync());
        return await LoadTransactionAsync(id);
    }

    /// <summary>Giao dịch lệch: ReconciliationRequired với đúng lý do, đã xử lý xong, và (trừ tiền ra) có đúng một sự cố mở cho giao dịch đó.</summary>
    private async Task AssertReconciliationRequiredAsync(PaymentTransaction transaction, string reason, bool expectIncident)
    {
        Assert.Equal(PaymentTransactionStatus.ReconciliationRequired, transaction.Status);
        Assert.Equal(reason, transaction.ReconciliationReason);
        Assert.NotNull(transaction.ProcessedAt);

        List<Incident> incidents = await LoadPaymentIncidentsAsync();
        int matching = 0;
        foreach (Incident incident in incidents)
        {
            if (incident.TargetId == transaction.Id)
            {
                matching++;
                Assert.Equal("PaymentTransaction", incident.TargetType);
                Assert.Equal(IncidentStatus.Open, incident.Status);
            }
        }
        if (expectIncident)
        {
            Assert.Equal(1, matching);
        }
        else
        {
            Assert.Equal(0, matching);
        }
    }

    [SqlFact]
    public async Task Ac90_Pay05_Webhook_MatchingPurchase_GrantsXsAndMarksTransactionMatched()
    {
        // Chuẩn bị: kho Free có eKYC, đơn XS 1 năm
        (HttpClient client, Guid vaultId, _) = await BillingTestData.CreateFreeVaultAsync(factory, 1);
        CreatedOrder order = await CreatePurchaseOrderAsync(client);
        long transactionId = NewTransactionId();
        Dictionary<string, object?> payload = PayloadFor(order, transactionId);

        // Hành động
        PaymentTransaction transaction = await SendAndLoadAsync(payload);

        // Kiểm tra: giao dịch Matched gắn đúng đơn
        Assert.Equal(PaymentTransactionStatus.Matched, transaction.Status);
        Assert.Equal(order.OrderId, transaction.PaymentOrderId);
        Assert.NotNull(transaction.ProcessedAt);
        Assert.Null(transaction.ReconciliationReason);

        // Kiểm tra: đơn Paid với PaidAt = giờ tiền vào
        PaymentOrder stored = await BillingTestData.LoadOrderAsync(factory, order.OrderId);
        Assert.Equal(PaymentOrderStatus.Paid, stored.Status);
        Assert.Equal(transaction.TransactionAt, stored.PaidAt);

        // Kiểm tra: kho nâng lên XS, hạn = giờ tiền vào + 365 ngày, kế hoạch vẫn DRAFT (SETUP-14)
        VaultEntitlement entitlement = await LoadEntitlementAsync(vaultId);
        Assert.Equal(ServiceTier.Xs, entitlement.Tier);
        Assert.Equal(EntitlementStatus.PaidActive, entitlement.Status);
        Assert.Equal(200 * 1048576L, entitlement.StorageQuotaBytes);
        Assert.Equal(transaction.TransactionAt!.Value.AddDays(365), entitlement.ServiceExpiresAt);
        JsonElement vault = await ApiTestHelpers.ReadJsonAsync(await client.GetAsync("/api/v1/vaults/me"));
        Assert.Equal("DRAFT", vault.GetProperty("planStatus").GetString());

        // Kiểm tra: nhật ký PAYMENT_PAID của hệ thống, không có sự cố
        Assert.Equal(1, await CountPaidLogsAsync(vaultId));
        Assert.Empty(await LoadPaymentIncidentsAsync());
    }

    [SqlFact]
    public async Task Addon02_Webhook_MatchingAddon_AddsSlotsAndKeepsExpiry()
    {
        // Chuẩn bị: XS còn 180 ngày, đơn mua thêm 1 suất
        (HttpClient client, Guid vaultId, _) = await BillingTestData.CreateFreeVaultAsync(factory, null);
        await BillingTestData.MakePaidActiveAsync(factory, vaultId, TimeSpan.FromDays(180));
        VaultEntitlement before = await LoadEntitlementAsync(vaultId);
        CreatedOrder order = await CreateAddonOrderAsync(client, 1);

        // Hành động
        PaymentTransaction transaction = await SendAndLoadAsync(PayloadFor(order, NewTransactionId()));

        // Kiểm tra
        Assert.Equal(PaymentTransactionStatus.Matched, transaction.Status);
        VaultEntitlement after = await LoadEntitlementAsync(vaultId);
        Assert.Equal(before.ExtraBeneficiarySlots + 1, after.ExtraBeneficiarySlots);
        Assert.Equal(before.ServiceExpiresAt, after.ServiceExpiresAt);
        Assert.Equal(PaymentOrderStatus.Paid, (await BillingTestData.LoadOrderAsync(factory, order.OrderId)).Status);
    }

    [SqlFact]
    public async Task Ac29_Webhook_RepeatedAfterMatch_DoesNotGrantTwice()
    {
        // Chuẩn bị: giao dịch đã khớp và cấp quyền
        (HttpClient client, Guid vaultId, _) = await BillingTestData.CreateFreeVaultAsync(factory, 1);
        CreatedOrder order = await CreatePurchaseOrderAsync(client);
        long transactionId = NewTransactionId();
        await SendAndLoadAsync(PayloadFor(order, transactionId));
        VaultEntitlement afterFirst = await LoadEntitlementAsync(vaultId);

        // Hành động: SePay gửi lại đúng giao dịch đó
        PaymentTransaction second = await SendAndLoadAsync(PayloadFor(order, transactionId));

        // Kiểm tra: vẫn 200, vẫn Matched, không cấp lần hai
        Assert.Equal(PaymentTransactionStatus.Matched, second.Status);
        VaultEntitlement afterSecond = await LoadEntitlementAsync(vaultId);
        Assert.Equal(afterFirst.ServiceExpiresAt, afterSecond.ServiceExpiresAt);
        Assert.Equal(1, await CountPaidLogsAsync(vaultId));
        int rows = 0;
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            rows = await db.PaymentTransactions.CountAsync();
        });
        Assert.Equal(1, rows);
    }

    [SqlFact]
    public async Task Ac97_Webhook_SecondTransactionForSameOrder_IsAlreadyPaidWithIncident()
    {
        // Chuẩn bị: giao dịch đầu đã khớp
        (HttpClient client, Guid vaultId, _) = await BillingTestData.CreateFreeVaultAsync(factory, 1);
        CreatedOrder order = await CreatePurchaseOrderAsync(client);
        PaymentTransaction first = await SendAndLoadAsync(PayloadFor(order, NewTransactionId()));
        VaultEntitlement afterFirst = await LoadEntitlementAsync(vaultId);

        // Hành động: người dùng chuyển thêm một lần nữa (ID giao dịch khác) cùng mã đơn
        PaymentTransaction second = await SendAndLoadAsync(PayloadFor(order, NewTransactionId()));

        // Kiểm tra: giao dịch hai chờ đối chiếu, không cấp thêm, đơn gắn đúng
        Assert.Equal(PaymentTransactionStatus.Matched, first.Status);
        await AssertReconciliationRequiredAsync(second, "ALREADY_PAID", true);
        Assert.Equal(order.OrderId, second.PaymentOrderId);
        VaultEntitlement afterSecond = await LoadEntitlementAsync(vaultId);
        Assert.Equal(afterFirst.ServiceExpiresAt, afterSecond.ServiceExpiresAt);
        Assert.Equal(1, await CountPaidLogsAsync(vaultId));
    }

    [SqlFact]
    public async Task Ac28_Webhook_UnderpaidAndOverpaid_AreAmountMismatchAndGrantNothing()
    {
        // Chuẩn bị: hai kho Free, mỗi kho một đơn
        (HttpClient underClient, Guid underVaultId, _) = await BillingTestData.CreateFreeVaultAsync(factory, 1);
        CreatedOrder underOrder = await CreatePurchaseOrderAsync(underClient);
        (HttpClient overClient, Guid overVaultId, _) = await BillingTestData.CreateFreeVaultAsync(factory, 1);
        CreatedOrder overOrder = await CreatePurchaseOrderAsync(overClient);

        // Hành động: một giao dịch thiếu 1 đồng, một giao dịch thừa 1 đồng
        Dictionary<string, object?> underPayload = Payload(
            NewTransactionId(), "chuyen tien " + underOrder.PaymentCode, underOrder.TotalAmount - 1, DateTimeOffset.UtcNow);
        Dictionary<string, object?> overPayload = Payload(
            NewTransactionId(), "chuyen tien " + overOrder.PaymentCode, overOrder.TotalAmount + 1, DateTimeOffset.UtcNow);
        PaymentTransaction under = await SendAndLoadAsync(underPayload);
        PaymentTransaction over = await SendAndLoadAsync(overPayload);

        // Kiểm tra: không cấp, đơn vẫn chờ, giao dịch gắn đơn để Admin thấy
        await AssertReconciliationRequiredAsync(under, "AMOUNT_MISMATCH", true);
        await AssertReconciliationRequiredAsync(over, "AMOUNT_MISMATCH", true);
        Assert.Equal(underOrder.OrderId, under.PaymentOrderId);
        Assert.Equal(overOrder.OrderId, over.PaymentOrderId);
        Assert.Equal(PaymentOrderStatus.Pending, (await BillingTestData.LoadOrderAsync(factory, underOrder.OrderId)).Status);
        Assert.Equal(PaymentOrderStatus.Pending, (await BillingTestData.LoadOrderAsync(factory, overOrder.OrderId)).Status);
        Assert.Equal(EntitlementStatus.Free, (await LoadEntitlementAsync(underVaultId)).Status);
        Assert.Equal(EntitlementStatus.Free, (await LoadEntitlementAsync(overVaultId)).Status);
    }

    [SqlFact]
    public async Task Pay03_Webhook_MoneyIntoConfiguredVirtualAccount_IsMatchedEvenWhenMainAccountDiffers()
    {
        // Chuẩn bị: SePay gửi tài khoản chính ở accountNumber và số VA đã cấu hình ở subAccount
        (HttpClient client, Guid vaultId, _) = await BillingTestData.CreateFreeVaultAsync(factory, 1);
        CreatedOrder order = await CreatePurchaseOrderAsync(client);
        Dictionary<string, object?> payload = PayloadFor(order, NewTransactionId());
        payload["accountNumber"] = "9999999999";
        payload["subAccount"] = BankAccountNumber();

        // Hành động
        PaymentTransaction transaction = await SendAndLoadAsync(payload);

        // Kiểm tra: khớp đơn, cấp quyền, và không lưu số tài khoản chính
        Assert.Equal(PaymentTransactionStatus.Matched, transaction.Status);
        Assert.Equal(order.OrderId, transaction.PaymentOrderId);
        Assert.Equal(BankAccountNumber(), transaction.AccountNumber);
        Assert.Equal(PaymentOrderStatus.Paid, (await BillingTestData.LoadOrderAsync(factory, order.OrderId)).Status);
        Assert.Equal(EntitlementStatus.PaidActive, (await LoadEntitlementAsync(vaultId)).Status);
    }

    [SqlFact]
    public async Task Pay03_Webhook_WrongVirtualAccount_IsWrongAccountEvenWhenMainAccountMatches()
    {
        // Chuẩn bị: accountNumber trùng cấu hình nhưng tiền vào một VA khác thì vẫn là sai tài khoản
        (HttpClient client, Guid vaultId, _) = await BillingTestData.CreateFreeVaultAsync(factory, 1);
        CreatedOrder order = await CreatePurchaseOrderAsync(client);
        Dictionary<string, object?> payload = PayloadFor(order, NewTransactionId());
        payload["subAccount"] = "96247OTHER1";

        // Hành động
        PaymentTransaction transaction = await SendAndLoadAsync(payload);

        // Kiểm tra
        await AssertReconciliationRequiredAsync(transaction, "WRONG_ACCOUNT", true);
        Assert.Null(transaction.PaymentOrderId);
        Assert.Equal(EntitlementStatus.Free, (await LoadEntitlementAsync(vaultId)).Status);
    }

    [SqlFact]
    public async Task Pay03_Webhook_WrongAccount_IsReconciliationRequiredAndGrantsNothing()
    {
        // Chuẩn bị
        (HttpClient client, Guid vaultId, _) = await BillingTestData.CreateFreeVaultAsync(factory, 1);
        CreatedOrder order = await CreatePurchaseOrderAsync(client);
        Dictionary<string, object?> payload = PayloadFor(order, NewTransactionId());
        payload["accountNumber"] = "9999999999";

        // Hành động
        PaymentTransaction transaction = await SendAndLoadAsync(payload);

        // Kiểm tra: sai tài khoản thì chưa gắn đơn
        await AssertReconciliationRequiredAsync(transaction, "WRONG_ACCOUNT", true);
        Assert.Null(transaction.PaymentOrderId);
        Assert.Equal(PaymentOrderStatus.Pending, (await BillingTestData.LoadOrderAsync(factory, order.OrderId)).Status);
        Assert.Equal(EntitlementStatus.Free, (await LoadEntitlementAsync(vaultId)).Status);
    }

    [SqlFact]
    public async Task Pay02_Webhook_NoCodeOrUnknownCode_AreCodeMissingAndOrderNotFound()
    {
        // Chuẩn bị: nội dung không có mã, và nội dung có mã hợp lệ nhưng không thuộc đơn nào
        Dictionary<string, object?> noCode = Payload(NewTransactionId(), "tien an trua", 199000, DateTimeOffset.UtcNow);
        Dictionary<string, object?> unknownCode = Payload(NewTransactionId(), "LVZZZZZZ99", 199000, DateTimeOffset.UtcNow);

        // Hành động
        PaymentTransaction missing = await SendAndLoadAsync(noCode);
        PaymentTransaction notFound = await SendAndLoadAsync(unknownCode);

        // Kiểm tra
        await AssertReconciliationRequiredAsync(missing, "CODE_MISSING", true);
        await AssertReconciliationRequiredAsync(notFound, "ORDER_NOT_FOUND", true);
        Assert.Null(missing.PaymentOrderId);
        Assert.Null(notFound.PaymentOrderId);
    }

    [SqlFact]
    public async Task Pay07_Webhook_CancelledOrder_IsOrderCancelledAndGrantsNothing()
    {
        // Chuẩn bị: Owner hủy đơn rồi mới chuyển tiền
        (HttpClient client, Guid vaultId, _) = await BillingTestData.CreateFreeVaultAsync(factory, 1);
        CreatedOrder order = await CreatePurchaseOrderAsync(client);
        HttpResponseMessage cancel = await client.PostAsync($"/api/v1/billing/orders/{order.OrderId}/cancel", null);
        Assert.Equal(HttpStatusCode.OK, cancel.StatusCode);

        // Hành động
        PaymentTransaction transaction = await SendAndLoadAsync(PayloadFor(order, NewTransactionId()));

        // Kiểm tra
        await AssertReconciliationRequiredAsync(transaction, "ORDER_CANCELLED", true);
        Assert.Equal(order.OrderId, transaction.PaymentOrderId);
        Assert.Equal(PaymentOrderStatus.Cancelled, (await BillingTestData.LoadOrderAsync(factory, order.OrderId)).Status);
        Assert.Equal(EntitlementStatus.Free, (await LoadEntitlementAsync(vaultId)).Status);
    }

    [SqlFact]
    public async Task Ac30_Webhook_TransactionAfterExpiry_IsLateButStillStored()
    {
        // Chuẩn bị: tiền vào sau hạn đơn 1 phút (giờ tiền vào, không phải giờ server nhận)
        (HttpClient client, Guid vaultId, _) = await BillingTestData.CreateFreeVaultAsync(factory, 1);
        CreatedOrder order = await CreatePurchaseOrderAsync(client);
        PaymentOrder stored = await BillingTestData.LoadOrderAsync(factory, order.OrderId);
        Dictionary<string, object?> payload = Payload(
            NewTransactionId(), "chuyen tien " + order.PaymentCode, order.TotalAmount, stored.ExpiresAt.AddMinutes(1));

        // Hành động
        PaymentTransaction transaction = await SendAndLoadAsync(payload);

        // Kiểm tra: giao dịch vẫn được lưu để đối chiếu, không cấp
        await AssertReconciliationRequiredAsync(transaction, "LATE_AFTER_EXPIRY", true);
        Assert.Equal(order.OrderId, transaction.PaymentOrderId);
        Assert.Equal(EntitlementStatus.Free, (await LoadEntitlementAsync(vaultId)).Status);
    }

    [SqlFact]
    public async Task Ac97_Pay06_Webhook_ExpiredOrderButMoneyArrivedBeforeExpiry_IsGranted()
    {
        // Chuẩn bị: đơn đã quá hạn và bị đánh dấu Expired, nhưng tiền vào cách đây 10 phút (trước hạn)
        (HttpClient client, Guid vaultId, _) = await BillingTestData.CreateFreeVaultAsync(factory, 1);
        CreatedOrder order = await CreatePurchaseOrderAsync(client);
        await BillingTestData.MakeOrderOverdueAsync(factory, order.OrderId);
        await BillingTestData.SetOrderStatusAsync(factory, order.OrderId, PaymentOrderStatus.Expired);
        Dictionary<string, object?> payload = Payload(
            NewTransactionId(), "chuyen tien " + order.PaymentCode, order.TotalAmount, DateTimeOffset.UtcNow.AddMinutes(-10));

        // Hành động
        PaymentTransaction transaction = await SendAndLoadAsync(payload);

        // Kiểm tra: cấp quyền, đơn thành Paid
        Assert.Equal(PaymentTransactionStatus.Matched, transaction.Status);
        Assert.Equal(PaymentOrderStatus.Paid, (await BillingTestData.LoadOrderAsync(factory, order.OrderId)).Status);
        Assert.Equal(EntitlementStatus.PaidActive, (await LoadEntitlementAsync(vaultId)).Status);
    }

    [SqlFact]
    public async Task Pay06_Webhook_UnreadableTransactionDate_IsTimeUnknown()
    {
        // Chuẩn bị
        (HttpClient client, Guid vaultId, _) = await BillingTestData.CreateFreeVaultAsync(factory, 1);
        CreatedOrder order = await CreatePurchaseOrderAsync(client);
        Dictionary<string, object?> payload = PayloadFor(order, NewTransactionId());
        payload["transactionDate"] = "hôm qua";

        // Hành động
        PaymentTransaction transaction = await SendAndLoadAsync(payload);

        // Kiểm tra: không biết giờ thì không tự cấp
        await AssertReconciliationRequiredAsync(transaction, "TIME_UNKNOWN", true);
        Assert.Equal(EntitlementStatus.Free, (await LoadEntitlementAsync(vaultId)).Status);
    }

    [SqlFact]
    public async Task Ac104_Webhook_ClosedVault_IsVaultClosedAndGrantsNothing()
    {
        // Chuẩn bị: kho đóng sau khi tạo đơn
        (HttpClient client, Guid vaultId, _) = await BillingTestData.CreateFreeVaultAsync(factory, 1);
        CreatedOrder order = await CreatePurchaseOrderAsync(client);
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            await db.Vaults.Where(v => v.Id == vaultId)
                .ExecuteUpdateAsync(s => s.SetProperty(v => v.Status, VaultStatus.Closed));
        });

        // Hành động
        PaymentTransaction transaction = await SendAndLoadAsync(PayloadFor(order, NewTransactionId()));

        // Kiểm tra
        await AssertReconciliationRequiredAsync(transaction, "VAULT_CLOSED", true);
        Assert.Equal(order.OrderId, transaction.PaymentOrderId);
        Assert.Equal(EntitlementStatus.Free, (await LoadEntitlementAsync(vaultId)).Status);
        Assert.Equal(PaymentOrderStatus.Pending, (await BillingTestData.LoadOrderAsync(factory, order.OrderId)).Status);
        Assert.Equal(0, await CountPaidLogsAsync(vaultId));
    }

    [SqlFact]
    public async Task Addon03_Webhook_AddonWhenServiceExpiryChanged_IsEntitlementChanged()
    {
        // Chuẩn bị: sau khi tạo đơn mua thêm, hạn kỳ dịch vụ bị đổi
        (HttpClient client, Guid vaultId, _) = await BillingTestData.CreateFreeVaultAsync(factory, null);
        await BillingTestData.MakePaidActiveAsync(factory, vaultId, TimeSpan.FromDays(180));
        CreatedOrder order = await CreateAddonOrderAsync(client, 1);
        await BillingTestData.MakePaidActiveAsync(factory, vaultId, TimeSpan.FromDays(400));

        // Hành động
        PaymentTransaction transaction = await SendAndLoadAsync(PayloadFor(order, NewTransactionId()));

        // Kiểm tra: không tự cấp suất
        await AssertReconciliationRequiredAsync(transaction, "ENTITLEMENT_CHANGED", true);
        Assert.Equal(0, (await LoadEntitlementAsync(vaultId)).ExtraBeneficiarySlots);
        Assert.Equal(PaymentOrderStatus.Pending, (await BillingTestData.LoadOrderAsync(factory, order.OrderId)).Status);
    }

    [SqlFact]
    public async Task Pay07_Webhook_OutgoingTransfer_IsNotIncomingWithoutIncident()
    {
        // Chuẩn bị: tiền ra tài khoản nhưng nội dung có mã của một đơn đang chờ
        (HttpClient client, Guid vaultId, _) = await BillingTestData.CreateFreeVaultAsync(factory, 1);
        CreatedOrder order = await CreatePurchaseOrderAsync(client);
        Dictionary<string, object?> payload = PayloadFor(order, NewTransactionId());
        payload["transferType"] = "out";

        // Hành động
        PaymentTransaction transaction = await SendAndLoadAsync(payload);

        // Kiểm tra: ghi nhận để đối chiếu nhưng không mở sự cố, không gắn đơn, không cấp
        await AssertReconciliationRequiredAsync(transaction, "NOT_INCOMING", false);
        Assert.Null(transaction.PaymentOrderId);
        Assert.Equal(PaymentOrderStatus.Pending, (await BillingTestData.LoadOrderAsync(factory, order.OrderId)).Status);
        Assert.Equal(EntitlementStatus.Free, (await LoadEntitlementAsync(vaultId)).Status);
        Assert.Empty(await LoadPaymentIncidentsAsync());
    }

    [SqlFact]
    public async Task Sec07_Webhook_PaymentIncident_HasOnlyReasonAndNoSensitiveData()
    {
        // Chuẩn bị: giao dịch thiếu tiền của một đơn có kho
        (HttpClient client, Guid vaultId, _) = await BillingTestData.CreateFreeVaultAsync(factory, 1);
        CreatedOrder order = await CreatePurchaseOrderAsync(client);
        Dictionary<string, object?> payload = Payload(
            NewTransactionId(), "chuyen tien " + order.PaymentCode, order.TotalAmount - 1, DateTimeOffset.UtcNow);

        // Hành động
        PaymentTransaction transaction = await SendAndLoadAsync(payload);

        // Kiểm tra: một sự cố PAYMENT mở cho giao dịch, gắn đúng kho, nội dung chỉ có mã lý do
        List<Incident> incidents = await LoadPaymentIncidentsAsync();
        Assert.Single(incidents);
        Incident incident = incidents[0];
        Assert.Equal(transaction.Id, incident.TargetId);
        Assert.Equal("PaymentTransaction", incident.TargetType);
        Assert.Equal(vaultId, incident.VaultId);
        Assert.Equal("Giao dịch thanh toán cần đối chiếu, lý do: AMOUNT_MISMATCH", incident.Summary);
        Assert.DoesNotContain(order.PaymentCode, incident.Summary);
        Assert.DoesNotContain(BankAccountNumber(), incident.Summary);
        Assert.DoesNotContain(order.TotalAmount.ToString(CultureInfo.InvariantCulture), incident.Summary);
    }

    [SqlFact]
    public async Task Pay05_Webhook_TransactionLeftReceived_IsReconciledWhenSePayRetries()
    {
        // Chuẩn bị: giả lập lần lưu thứ hai bị lỗi lần trước, nên giao dịch nằm lại ở Received trong DB
        (HttpClient client, Guid vaultId, _) = await BillingTestData.CreateFreeVaultAsync(factory, 1);
        CreatedOrder order = await CreatePurchaseOrderAsync(client);
        long transactionId = NewTransactionId();
        DateTimeOffset at = DateTimeOffset.UtcNow;
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            PaymentTransaction stuck = new PaymentTransaction();
            stuck.Provider = "SePay";
            stuck.Environment = "Test";
            stuck.ProviderTransactionId = transactionId;
            stuck.AccountNumber = BankAccountNumber();
            stuck.MatchedPaymentCode = order.PaymentCode;
            stuck.TransferType = "in";
            stuck.TransferAmount = order.TotalAmount;
            stuck.TransactionAt = at;
            stuck.ReceivedAt = at;
            stuck.Status = PaymentTransactionStatus.Received;
            db.PaymentTransactions.Add(stuck);
            await db.SaveChangesAsync();
        });

        // Hành động: SePay gửi lại cùng giao dịch
        Dictionary<string, object?> payload = Payload(transactionId, "chuyen tien " + order.PaymentCode, order.TotalAmount, at);
        PaymentTransaction transaction = await SendAndLoadAsync(payload);

        // Kiểm tra: xử lý xong trên đúng dòng cũ
        Assert.Equal(PaymentTransactionStatus.Matched, transaction.Status);
        Assert.Equal(order.OrderId, transaction.PaymentOrderId);
        Assert.Equal(PaymentOrderStatus.Paid, (await BillingTestData.LoadOrderAsync(factory, order.OrderId)).Status);
        Assert.Equal(EntitlementStatus.PaidActive, (await LoadEntitlementAsync(vaultId)).Status);
        int rows = 0;
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            rows = await db.PaymentTransactions.CountAsync();
        });
        Assert.Equal(1, rows);
    }

    [SqlFact]
    public async Task Pay04_Ac29_Webhook_SameMatchingTransactionInParallel_GrantsOnlyOnce()
    {
        // Chuẩn bị
        (HttpClient client, Guid vaultId, _) = await BillingTestData.CreateFreeVaultAsync(factory, 1);
        CreatedOrder order = await CreatePurchaseOrderAsync(client);
        long transactionId = NewTransactionId();
        List<Task<HttpResponseMessage>> requests = new List<Task<HttpResponseMessage>>();

        // Hành động: năm request cùng giao dịch khớp chạy song song
        for (int i = 0; i < 5; i++)
        {
            requests.Add(SendAsync(PayloadFor(order, transactionId)));
        }
        HttpResponseMessage[] responses = await Task.WhenAll(requests);

        // Kiểm tra: không request nào lỗi 5xx; bên thua có thể nhận 409 để SePay gửi lại
        int succeeded = 0;
        foreach (HttpResponseMessage response in responses)
        {
            bool acceptable = response.StatusCode == HttpStatusCode.OK || response.StatusCode == HttpStatusCode.Conflict;
            Assert.True(acceptable, "Trạng thái không mong đợi: " + response.StatusCode);
            if (response.StatusCode == HttpStatusCode.OK)
            {
                succeeded++;
            }
        }
        Assert.True(succeeded >= 1);

        // SePay gửi lại lần nữa (nếu có bên thua 409): kết quả cuối vẫn là một lần cấp duy nhất
        PaymentTransaction transaction = await SendAndLoadAsync(PayloadFor(order, transactionId));
        Assert.Equal(PaymentTransactionStatus.Matched, transaction.Status);
        Assert.Equal(PaymentOrderStatus.Paid, (await BillingTestData.LoadOrderAsync(factory, order.OrderId)).Status);
        Assert.Equal(1, await CountPaidLogsAsync(vaultId));
        Assert.Empty(await LoadPaymentIncidentsAsync());
        int rows = 0;
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            rows = await db.PaymentTransactions.CountAsync();
        });
        Assert.Equal(1, rows);
    }
}
