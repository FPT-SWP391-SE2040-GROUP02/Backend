using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LegacyVault.Application.Services;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;
using LegacyVault.IntegrationTests.DeathCases;
using LegacyVault.IntegrationTests.TestSupport;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace LegacyVault.IntegrationTests.Payment;

/// <summary>
/// API Admin xem giao dịch thanh toán (SRS ADMIN-09, PAY-07, AC-75): chỉ đọc, chỉ Admin đang hoạt động, không lộ nội dung
/// chuyển khoản, số tài khoản hay secret. Chạy API thật trên SQL Server database tạm.
/// </summary>
public sealed class AdminPaymentTransactionsTests : IAsyncLifetime
{
    private const string ListUrl = "/api/v1/admin/payment-transactions";

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

    private HttpClient ClientFor(User user)
    {
        HttpClient client = factory.CreateClient();
        ApiTestHelpers.AuthorizeAs(client, factory.Services, user);
        return client;
    }

    private async Task<HttpClient> AdminClientAsync()
    {
        User admin = await DeathCaseTestData.AddUserAsync(factory, UserRole.Admin, UserStatus.Active);
        return ClientFor(admin);
    }

    /// <summary>Chèn thẳng một giao dịch với trạng thái và thời điểm nhận cho trước (số tiền, nội dung, số tài khoản giả).</summary>
    private async Task<Guid> AddTransactionAsync(
        long providerTransactionId, PaymentTransactionStatus status, string? reason, DateTimeOffset receivedAt)
    {
        PaymentTransaction transaction = new PaymentTransaction();
        transaction.Provider = "SePay";
        transaction.Environment = "Test";
        transaction.ProviderTransactionId = providerTransactionId;
        transaction.Gateway = "MBBank";
        transaction.AccountNumber = "0000000000";
        transaction.Code = null;
        transaction.MatchedPaymentCode = "LVAAAAAA22";
        transaction.Content = "noi dung chuyen khoan rieng tu";
        transaction.TransferType = "in";
        transaction.TransferAmount = 199000;
        transaction.ReferenceCode = "FT26289000001";
        transaction.TransactionAt = receivedAt;
        transaction.ReceivedAt = receivedAt;
        transaction.Status = status;
        transaction.ReconciliationReason = reason;
        if (status != PaymentTransactionStatus.Received)
        {
            transaction.ProcessedAt = receivedAt;
        }
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            db.PaymentTransactions.Add(transaction);
            await db.SaveChangesAsync();
        });
        return transaction.Id;
    }

    [SqlFact]
    public async Task Admin09_Admin_ListsTransactionsNewestFirstWithoutSensitiveFields()
    {
        // Chuẩn bị: hai giao dịch, giao dịch 2 nhận sau
        DateTimeOffset now = DateTimeOffset.UtcNow;
        Guid olderId = await AddTransactionAsync(1, PaymentTransactionStatus.Matched, null, now.AddMinutes(-10));
        Guid newerId = await AddTransactionAsync(2, PaymentTransactionStatus.ReconciliationRequired, "AMOUNT_MISMATCH", now);
        HttpClient admin = await AdminClientAsync();

        // Hành động
        HttpResponseMessage response = await admin.GetAsync(ListUrl);

        // Kiểm tra
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        string rawBody = await response.Content.ReadAsStringAsync();
        JsonElement page = JsonDocument.Parse(rawBody).RootElement.Clone();
        Assert.Equal(2, page.GetProperty("totalCount").GetInt32());
        JsonElement items = page.GetProperty("items");
        Assert.Equal(newerId, items[0].GetProperty("id").GetGuid());
        Assert.Equal(olderId, items[1].GetProperty("id").GetGuid());

        JsonElement first = items[0];
        Assert.Equal("SePay", first.GetProperty("provider").GetString());
        Assert.Equal("Test", first.GetProperty("environment").GetString());
        Assert.Equal(2, first.GetProperty("providerTransactionId").GetInt64());
        Assert.Equal("RECONCILIATION_REQUIRED", first.GetProperty("status").GetString());
        Assert.Equal("AMOUNT_MISMATCH", first.GetProperty("reconciliationReason").GetString());
        Assert.Equal("IN", first.GetProperty("transferType").GetString());
        Assert.Equal(199000, first.GetProperty("transferAmount").GetInt64());
        Assert.Equal("VND", first.GetProperty("currency").GetString());
        Assert.Equal("LVAAAAAA22", first.GetProperty("paymentCode").GetString());
        Assert.Equal("FT26289000001", first.GetProperty("referenceCode").GetString());
        Assert.Equal("MATCHED", items[1].GetProperty("status").GetString());

        // Kiểm tra: không lộ nội dung chuyển khoản, số tài khoản, RowVersion (ADMIN-09, AC-75)
        Assert.False(first.TryGetProperty("content", out _));
        Assert.False(first.TryGetProperty("accountNumber", out _));
        Assert.False(first.TryGetProperty("rowVersion", out _));
        Assert.DoesNotContain("noi dung chuyen khoan rieng tu", rawBody);
        Assert.DoesNotContain("0000000000", rawBody);
    }

    [SqlFact]
    public async Task Admin09_Admin_FiltersByStatus_AndRejectsUnknownStatus()
    {
        // Chuẩn bị
        DateTimeOffset now = DateTimeOffset.UtcNow;
        await AddTransactionAsync(1, PaymentTransactionStatus.Matched, null, now.AddMinutes(-3));
        Guid lateId = await AddTransactionAsync(2, PaymentTransactionStatus.ReconciliationRequired, "LATE_AFTER_EXPIRY", now.AddMinutes(-2));
        Guid wrongId = await AddTransactionAsync(3, PaymentTransactionStatus.ReconciliationRequired, "WRONG_ACCOUNT", now.AddMinutes(-1));
        await AddTransactionAsync(4, PaymentTransactionStatus.Received, null, now);
        HttpClient admin = await AdminClientAsync();

        // Hành động
        HttpResponseMessage filtered = await admin.GetAsync(ListUrl + "?status=RECONCILIATION_REQUIRED");
        HttpResponseMessage unknown = await admin.GetAsync(ListUrl + "?status=PAID");

        // Kiểm tra: chỉ hai giao dịch lệch, mới nhất trước; không có trạng thái PAID để lọc (PAY-07)
        Assert.Equal(HttpStatusCode.OK, filtered.StatusCode);
        JsonElement page = await ApiTestHelpers.ReadJsonAsync(filtered);
        Assert.Equal(2, page.GetProperty("totalCount").GetInt32());
        Assert.Equal(wrongId, page.GetProperty("items")[0].GetProperty("id").GetGuid());
        Assert.Equal(lateId, page.GetProperty("items")[1].GetProperty("id").GetGuid());
        await ApiTestHelpers.AssertProblemAsync(unknown, HttpStatusCode.BadRequest, "VALIDATION_ERROR");
    }

    [SqlFact]
    public async Task Admin09_Admin_PagesResults()
    {
        // Chuẩn bị: 5 giao dịch
        DateTimeOffset now = DateTimeOffset.UtcNow;
        for (int i = 1; i <= 5; i++)
        {
            await AddTransactionAsync(i, PaymentTransactionStatus.Received, null, now.AddMinutes(i));
        }
        HttpClient admin = await AdminClientAsync();

        // Hành động
        JsonElement firstPage = await ApiTestHelpers.ReadJsonAsync(await admin.GetAsync(ListUrl + "?pageIndex=1&pageSize=2"));
        JsonElement lastPage = await ApiTestHelpers.ReadJsonAsync(await admin.GetAsync(ListUrl + "?pageIndex=3&pageSize=2"));

        // Kiểm tra
        Assert.Equal(5, firstPage.GetProperty("totalCount").GetInt32());
        Assert.Equal(3, firstPage.GetProperty("totalPages").GetInt32());
        Assert.Equal(2, firstPage.GetProperty("items").GetArrayLength());
        Assert.True(firstPage.GetProperty("hasNextPage").GetBoolean());
        Assert.False(firstPage.GetProperty("hasPreviousPage").GetBoolean());
        Assert.Equal(5, firstPage.GetProperty("items")[0].GetProperty("providerTransactionId").GetInt64());
        Assert.Equal(1, lastPage.GetProperty("items").GetArrayLength());
        Assert.False(lastPage.GetProperty("hasNextPage").GetBoolean());
        Assert.True(lastPage.GetProperty("hasPreviousPage").GetBoolean());
        Assert.Equal(1, lastPage.GetProperty("items")[0].GetProperty("providerTransactionId").GetInt64());
    }

    [SqlFact]
    public async Task Sec04_NonAdminUsers_Get404_AndAnonymousGets401()
    {
        // Chuẩn bị
        await AddTransactionAsync(1, PaymentTransactionStatus.Received, null, DateTimeOffset.UtcNow);
        User plain = await DeathCaseTestData.AddUserAsync(factory, UserRole.User, UserStatus.Active);
        User verifier = await DeathCaseTestData.AddVerifierAsync(factory);
        User lockedAdmin = await DeathCaseTestData.AddUserAsync(factory, UserRole.Admin, UserStatus.Locked);

        // Hành động + Kiểm tra: Owner thường, Verifier, Admin bị khóa đều như khu vực không tồn tại
        foreach (User user in new User[] { plain, verifier, lockedAdmin })
        {
            HttpResponseMessage response = await ClientFor(user).GetAsync(ListUrl);
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateClient().GetAsync(ListUrl)).StatusCode);
    }

    [SqlFact]
    public async Task Pay07_NoWriteEndpointsExist_AdminCannotMarkTransactionPaid()
    {
        // Chuẩn bị
        Guid transactionId = await AddTransactionAsync(
            1, PaymentTransactionStatus.ReconciliationRequired, "AMOUNT_MISMATCH", DateTimeOffset.UtcNow);
        HttpClient admin = await AdminClientAsync();
        StringContent empty = new StringContent("{}", System.Text.Encoding.UTF8, "application/json");

        // Hành động
        HttpResponseMessage post = await admin.PostAsync(ListUrl, empty);
        HttpResponseMessage put = await admin.PutAsync(ListUrl + "/" + transactionId, empty);
        HttpResponseMessage delete = await admin.DeleteAsync(ListUrl + "/" + transactionId);
        HttpResponseMessage markPaid = await admin.PostAsync(ListUrl + "/" + transactionId + "/paid", empty);

        // Kiểm tra: không có route ghi nào; giao dịch giữ nguyên
        Assert.Equal(HttpStatusCode.MethodNotAllowed, post.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, put.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, delete.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, markPaid.StatusCode);
        JsonElement page = await ApiTestHelpers.ReadJsonAsync(await admin.GetAsync(ListUrl));
        Assert.Equal("RECONCILIATION_REQUIRED", page.GetProperty("items")[0].GetProperty("status").GetString());
    }

    [SqlFact]
    public async Task Admin09_EndToEnd_MismatchedWebhookAppearsInAdminList()
    {
        // Chuẩn bị: một webhook không khớp đơn nào (mã lạ) đi qua API thật
        string bankAccount;
        using (IServiceScope scope = factory.Services.CreateScope())
        {
            bankAccount = scope.ServiceProvider.GetRequiredService<IBillingBankSettings>().AccountNumber;
        }
        Dictionary<string, object?> payload = new Dictionary<string, object?>();
        payload["id"] = 987001;
        payload["gateway"] = "MBBank";
        payload["transactionDate"] = DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(7)).ToString("yyyy-MM-dd HH:mm:ss");
        payload["accountNumber"] = bankAccount;
        payload["content"] = "LVZZZZZZ99";
        payload["transferType"] = "in";
        payload["transferAmount"] = 199000;
        HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/billing/sepay/webhook");
        request.Content = JsonContent.Create(payload);
        request.Headers.TryAddWithoutValidation("Authorization", "Apikey " + ApiFactory.SePayTestApiKey);
        HttpResponseMessage webhook = await factory.CreateClient().SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, webhook.StatusCode);
        HttpClient admin = await AdminClientAsync();

        // Hành động
        JsonElement page = await ApiTestHelpers.ReadJsonAsync(await admin.GetAsync(ListUrl + "?status=RECONCILIATION_REQUIRED"));

        // Kiểm tra
        Assert.Equal(1, page.GetProperty("totalCount").GetInt32());
        JsonElement item = page.GetProperty("items")[0];
        Assert.Equal(987001, item.GetProperty("providerTransactionId").GetInt64());
        Assert.Equal("ORDER_NOT_FOUND", item.GetProperty("reconciliationReason").GetString());
        Assert.Equal("LVZZZZZZ99", item.GetProperty("paymentCode").GetString());
        Assert.Equal(JsonValueKind.Null, item.GetProperty("paymentOrderId").ValueKind);
    }
}
