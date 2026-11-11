using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;
using LegacyVault.IntegrationTests.DeathCases;
using LegacyVault.IntegrationTests.TestSupport;
using Xunit;

namespace LegacyVault.IntegrationTests.Payment;

/// <summary>
/// API doanh thu của Admin: chỉ cộng giao dịch đã khớp đơn hoặc đã xử lý, mặc định chỉ tiền thật (Live), gom theo ngày,
/// tháng, quý theo giờ Việt Nam, chỉ Admin đang hoạt động. Chạy API thật trên SQL Server database tạm.
/// </summary>
public sealed class AdminRevenueTests : IAsyncLifetime
{
    private const string RevenueUrl = "/api/v1/admin/payment-transactions/revenue";
    private static readonly TimeSpan Vietnam = TimeSpan.FromHours(7);

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

    private async Task<HttpClient> ClientForRoleAsync(UserRole role)
    {
        User user = await DeathCaseTestData.AddUserAsync(factory, role, UserStatus.Active);
        HttpClient client = factory.CreateClient();
        ApiTestHelpers.AuthorizeAs(client, factory.Services, user);
        return client;
    }

    private async Task AddTransactionAsync(
        long providerTransactionId, PaymentTransactionStatus status, long amount, DateTimeOffset receivedAt,
        string environment = "Live")
    {
        PaymentTransaction transaction = new PaymentTransaction();
        transaction.Provider = "SePay";
        transaction.Environment = environment;
        transaction.ProviderTransactionId = providerTransactionId;
        transaction.AccountNumber = "0000000000";
        transaction.TransferType = "in";
        transaction.TransferAmount = amount;
        transaction.TransactionAt = receivedAt;
        transaction.ReceivedAt = receivedAt;
        transaction.Status = status;
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            db.PaymentTransactions.Add(transaction);
            await db.SaveChangesAsync();
        });
    }

    [SqlFact]
    public async Task Revenue_SumsOnlyMatchedAndResolved_AndOnlyLiveByDefault()
    {
        // Chuẩn bị (chỉ một giao dịch Matched vì chỉ mục unique theo đơn): hai giao dịch thành công thật, một lệch, một chưa đối chiếu, một thành công nhưng là thử nghiệm
        DateTimeOffset day = new DateTimeOffset(2026, 11, 5, 10, 0, 0, Vietnam);
        await AddTransactionAsync(1, PaymentTransactionStatus.Matched, 199000, day);
        await AddTransactionAsync(2, PaymentTransactionStatus.Resolved, 49000, day.AddHours(1));
        await AddTransactionAsync(3, PaymentTransactionStatus.ReconciliationRequired, 999000, day.AddHours(2));
        await AddTransactionAsync(4, PaymentTransactionStatus.Received, 555000, day.AddHours(3));
        await AddTransactionAsync(5, PaymentTransactionStatus.Resolved, 399000, day.AddHours(4), "Test");
        HttpClient admin = await ClientForRoleAsync(UserRole.Admin);

        // Hành động
        HttpResponseMessage response = await admin.GetAsync(RevenueUrl);

        // Kiểm tra
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        JsonElement body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("month", body.GetProperty("groupBy").GetString());
        Assert.Equal("Live", body.GetProperty("environment").GetString());
        Assert.Equal(248000, body.GetProperty("totalAmount").GetInt64());
        Assert.Equal(2, body.GetProperty("transactionCount").GetInt32());
        Assert.Equal("VND", body.GetProperty("currency").GetString());
        JsonElement buckets = body.GetProperty("buckets");
        Assert.Equal(1, buckets.GetArrayLength());
        Assert.Equal("2026-11", buckets[0].GetProperty("key").GetString());
        Assert.Equal("11/2026", buckets[0].GetProperty("label").GetString());
    }

    [SqlFact]
    public async Task Revenue_EnvironmentTestAndAll_SelectTheRightTransactions()
    {
        // Chuẩn bị
        DateTimeOffset day = new DateTimeOffset(2026, 11, 5, 10, 0, 0, Vietnam);
        await AddTransactionAsync(1, PaymentTransactionStatus.Resolved, 100000, day, "Live");
        await AddTransactionAsync(2, PaymentTransactionStatus.Resolved, 20000, day, "Test");
        HttpClient admin = await ClientForRoleAsync(UserRole.Admin);

        // Hành động
        JsonElement test = await admin.GetFromJsonAsync<JsonElement>(RevenueUrl + "?environment=Test");
        JsonElement all = await admin.GetFromJsonAsync<JsonElement>(RevenueUrl + "?environment=ALL");
        HttpResponseMessage bad = await admin.GetAsync(RevenueUrl + "?environment=Prod");

        // Kiểm tra
        Assert.Equal(20000, test.GetProperty("totalAmount").GetInt64());
        Assert.Equal(120000, all.GetProperty("totalAmount").GetInt64());
        Assert.Equal("ALL", all.GetProperty("environment").GetString());
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
    }

    [SqlFact]
    public async Task Revenue_GroupsByDayMonthAndQuarter_InVietnamTime()
    {
        // Chuẩn bị: 23:30 ngày 30/09 giờ Việt Nam (vẫn là Quý 3), 00:30 ngày 01/10 (Quý 4), 12:00 ngày 02/10
        await AddTransactionAsync(1, PaymentTransactionStatus.Resolved, 10000, new DateTimeOffset(2026, 9, 30, 23, 30, 0, Vietnam));
        await AddTransactionAsync(2, PaymentTransactionStatus.Resolved, 20000, new DateTimeOffset(2026, 10, 1, 0, 30, 0, Vietnam));
        await AddTransactionAsync(3, PaymentTransactionStatus.Resolved, 30000, new DateTimeOffset(2026, 10, 2, 12, 0, 0, Vietnam));
        HttpClient admin = await ClientForRoleAsync(UserRole.Admin);

        // Hành động
        JsonElement byDay = await admin.GetFromJsonAsync<JsonElement>(RevenueUrl + "?groupBy=day");
        JsonElement byMonth = await admin.GetFromJsonAsync<JsonElement>(RevenueUrl + "?groupBy=month");
        JsonElement byQuarter = await admin.GetFromJsonAsync<JsonElement>(RevenueUrl + "?groupBy=quarter");
        HttpResponseMessage bad = await admin.GetAsync(RevenueUrl + "?groupBy=year");

        // Kiểm tra
        JsonElement days = byDay.GetProperty("buckets");
        Assert.Equal(3, days.GetArrayLength());
        Assert.Equal("2026-09-30", days[0].GetProperty("key").GetString());
        Assert.Equal("01/10/2026", days[1].GetProperty("label").GetString());
        Assert.Equal(30000, days[2].GetProperty("amount").GetInt64());

        JsonElement months = byMonth.GetProperty("buckets");
        Assert.Equal(2, months.GetArrayLength());
        Assert.Equal("2026-09", months[0].GetProperty("key").GetString());
        Assert.Equal(10000, months[0].GetProperty("amount").GetInt64());
        Assert.Equal(50000, months[1].GetProperty("amount").GetInt64());
        Assert.Equal(2, months[1].GetProperty("count").GetInt32());

        JsonElement quarters = byQuarter.GetProperty("buckets");
        Assert.Equal(2, quarters.GetArrayLength());
        Assert.Equal("2026-Q3", quarters[0].GetProperty("key").GetString());
        Assert.Equal("Quý 4/2026", quarters[1].GetProperty("label").GetString());
        Assert.Equal(60000, byQuarter.GetProperty("totalAmount").GetInt64());
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
    }

    [SqlFact]
    public async Task Revenue_FiltersByDateRange_BothEndsIncluded()
    {
        // Chuẩn bị
        await AddTransactionAsync(1, PaymentTransactionStatus.Resolved, 1000, new DateTimeOffset(2026, 11, 10, 23, 30, 0, Vietnam));
        await AddTransactionAsync(2, PaymentTransactionStatus.Resolved, 2000, new DateTimeOffset(2026, 11, 11, 0, 30, 0, Vietnam));
        await AddTransactionAsync(3, PaymentTransactionStatus.Resolved, 4000, new DateTimeOffset(2026, 11, 12, 23, 59, 0, Vietnam));
        await AddTransactionAsync(4, PaymentTransactionStatus.Resolved, 8000, new DateTimeOffset(2026, 11, 13, 0, 1, 0, Vietnam));
        HttpClient admin = await ClientForRoleAsync(UserRole.Admin);

        // Hành động
        JsonElement range = await admin.GetFromJsonAsync<JsonElement>(RevenueUrl + "?from=2026-11-11&to=2026-11-12");
        HttpResponseMessage reversed = await admin.GetAsync(RevenueUrl + "?from=2026-11-12&to=2026-11-10");
        HttpResponseMessage badDate = await admin.GetAsync(RevenueUrl + "?from=2026-13-40");

        // Kiểm tra: lấy 00:30 ngày 11 và 23:59 ngày 12, bỏ 23:30 ngày 10 và 00:01 ngày 13
        Assert.Equal(6000, range.GetProperty("totalAmount").GetInt64());
        Assert.Equal(2, range.GetProperty("transactionCount").GetInt32());
        Assert.Equal(HttpStatusCode.BadRequest, reversed.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, badDate.StatusCode);
    }

    [SqlFact]
    public async Task Revenue_NoTransactions_ReturnsZeroAndEmptyBuckets()
    {
        // Chuẩn bị
        HttpClient admin = await ClientForRoleAsync(UserRole.Admin);

        // Hành động
        JsonElement body = await admin.GetFromJsonAsync<JsonElement>(RevenueUrl);

        // Kiểm tra
        Assert.Equal(0, body.GetProperty("totalAmount").GetInt64());
        Assert.Equal(0, body.GetProperty("transactionCount").GetInt32());
        Assert.Equal(0, body.GetProperty("buckets").GetArrayLength());
    }

    [SqlFact]
    public async Task Revenue_NonAdmin_GetsNotFound()
    {
        // Chuẩn bị
        HttpClient owner = await ClientForRoleAsync(UserRole.User);

        // Hành động
        HttpResponseMessage response = await owner.GetAsync(RevenueUrl);

        // Kiểm tra: khu vực Admin không lộ sự tồn tại (SEC-04)
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
