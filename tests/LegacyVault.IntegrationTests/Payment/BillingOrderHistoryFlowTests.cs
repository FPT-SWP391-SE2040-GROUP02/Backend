using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;
using LegacyVault.IntegrationTests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace LegacyVault.IntegrationTests.Payment;

/// <summary>GET /api/v1/billing/orders: lịch sử đơn thanh toán của kho, phân trang, chỉ của chính Owner.</summary>
public sealed class BillingOrderHistoryFlowTests : IAsyncLifetime
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

    private static Task<HttpResponseMessage> ListAsync(HttpClient client, string query)
    {
        return client.GetAsync("/api/v1/billing/orders" + query);
    }

    private static async Task<Guid> CreateAddonOrderAsync(HttpClient client)
    {
        HttpResponseMessage response = await client.PostAsJsonAsync("/api/v1/billing/orders", new { extraSlots = 1 });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await ApiTestHelpers.ReadJsonAsync(response)).GetProperty("id").GetGuid();
    }

    /// <summary>Tạo một đơn rồi hủy; đơn Pending cũ phải hết thì mới tạo được đơn mới.</summary>
    private static async Task<Guid> CreateAndCancelAsync(HttpClient client)
    {
        Guid orderId = await CreateAddonOrderAsync(client);
        HttpResponseMessage cancel = await client.PostAsync($"/api/v1/billing/orders/{orderId}/cancel", null);
        Assert.Equal(HttpStatusCode.OK, cancel.StatusCode);
        return orderId;
    }

    [SqlFact]
    public async Task History_NoOrders_ReturnsEmptyPage()
    {
        // Chuẩn bị
        (HttpClient client, _, _) = await BillingTestData.CreateFreeVaultAsync(factory, null);

        // Hành động
        HttpResponseMessage response = await ListAsync(client, "");

        // Kiểm tra
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        JsonElement page = await ApiTestHelpers.ReadJsonAsync(response);
        Assert.Equal(0, page.GetProperty("totalCount").GetInt32());
        Assert.Equal(0, page.GetProperty("items").GetArrayLength());
    }

    [SqlFact]
    public async Task History_ListsOrdersNewestFirstWithSummaryFieldsOnly()
    {
        // Chuẩn bị: đơn 1 bị hủy, đơn 2 đã trả
        (HttpClient client, Guid vaultId, _) = await BillingTestData.CreateFreeVaultAsync(factory, null);
        await BillingTestData.MakePaidActiveAsync(factory, vaultId, TimeSpan.FromDays(100));
        Guid cancelledId = await CreateAndCancelAsync(client);
        Guid paidId = await CreateAddonOrderAsync(client);
        HttpResponseMessage paid = await client.PostAsync($"/api/v1/billing/orders/{paidId}/dev-pay", null);
        Assert.Equal(HttpStatusCode.OK, paid.StatusCode);

        // Hành động
        HttpResponseMessage response = await ListAsync(client, "?sortBy=createdAt&isAscending=false");

        // Kiểm tra: mới nhất trước; không có thông tin ngân hàng hay mã QR; có mã thanh toán làm mã tham chiếu
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        JsonElement page = await ApiTestHelpers.ReadJsonAsync(response);
        Assert.Equal(2, page.GetProperty("totalCount").GetInt32());
        JsonElement first = page.GetProperty("items")[0];
        JsonElement second = page.GetProperty("items")[1];
        Assert.Equal(paidId, first.GetProperty("id").GetGuid());
        Assert.Equal("PAID", first.GetProperty("status").GetString());
        Assert.NotEqual(JsonValueKind.Null, first.GetProperty("paidAt").ValueKind);
        Assert.Equal(cancelledId, second.GetProperty("id").GetGuid());
        Assert.Equal("CANCELLED", second.GetProperty("status").GetString());
        Assert.NotEqual(JsonValueKind.Null, second.GetProperty("cancelledAt").ValueKind);
        Assert.Equal("ADDON", first.GetProperty("kind").GetString());
        Assert.StartsWith("LV", first.GetProperty("paymentCode").GetString());
        Assert.Equal("VND", first.GetProperty("currency").GetString());
        Assert.True(first.GetProperty("totalAmount").GetInt64() > 0);
        Assert.False(first.TryGetProperty("bank", out _));
        Assert.False(first.TryGetProperty("qrImageUrl", out _));
        Assert.False(first.TryGetProperty("transferContent", out _));
    }

    [SqlFact]
    public async Task History_Paging_SplitsPages()
    {
        // Chuẩn bị: 2 đơn
        (HttpClient client, Guid vaultId, _) = await BillingTestData.CreateFreeVaultAsync(factory, null);
        await BillingTestData.MakePaidActiveAsync(factory, vaultId, TimeSpan.FromDays(100));
        await CreateAndCancelAsync(client);
        await CreateAddonOrderAsync(client);

        // Hành động
        HttpResponseMessage response = await ListAsync(client, "?pageIndex=2&pageSize=1&isAscending=false");

        // Kiểm tra
        JsonElement page = await ApiTestHelpers.ReadJsonAsync(response);
        Assert.Equal(1, page.GetProperty("items").GetArrayLength());
        Assert.Equal(2, page.GetProperty("totalCount").GetInt32());
        Assert.Equal(2, page.GetProperty("totalPages").GetInt32());
        Assert.False(page.GetProperty("hasNextPage").GetBoolean());
        Assert.True(page.GetProperty("hasPreviousPage").GetBoolean());
    }

    [SqlFact]
    public async Task History_OverduePendingOrder_ShowsExpiredWithoutWritingToDatabase()
    {
        // Chuẩn bị: đơn chờ đã quá hạn nhưng chưa ai đọc nên DB vẫn Pending (D7: không có job hết hạn)
        (HttpClient client, Guid vaultId, _) = await BillingTestData.CreateFreeVaultAsync(factory, null);
        await BillingTestData.MakePaidActiveAsync(factory, vaultId, TimeSpan.FromDays(100));
        Guid orderId = await CreateAddonOrderAsync(client);
        await BillingTestData.MakeOrderOverdueAsync(factory, orderId);

        // Hành động
        HttpResponseMessage response = await ListAsync(client, "");

        // Kiểm tra: hiện EXPIRED, nhưng đọc lịch sử không ghi gì
        JsonElement page = await ApiTestHelpers.ReadJsonAsync(response);
        Assert.Equal("EXPIRED", page.GetProperty("items")[0].GetProperty("status").GetString());
        PaymentOrder stored = await BillingTestData.LoadOrderAsync(factory, orderId);
        Assert.Equal(PaymentOrderStatus.Pending, stored.Status);
    }

    [SqlFact]
    public async Task History_ShowsOnlyOwnOrders()
    {
        // Chuẩn bị: hai Owner, mỗi người một đơn
        (HttpClient first, Guid firstVault, _) = await BillingTestData.CreateFreeVaultAsync(factory, null);
        await BillingTestData.MakePaidActiveAsync(factory, firstVault, TimeSpan.FromDays(100));
        Guid firstOrder = await CreateAddonOrderAsync(first);
        (HttpClient second, Guid secondVault, _) = await BillingTestData.CreateFreeVaultAsync(factory, null);
        await BillingTestData.MakePaidActiveAsync(factory, secondVault, TimeSpan.FromDays(100));
        Guid secondOrder = await CreateAddonOrderAsync(second);

        // Hành động
        JsonElement firstPage = await ApiTestHelpers.ReadJsonAsync(await ListAsync(first, ""));
        JsonElement secondPage = await ApiTestHelpers.ReadJsonAsync(await ListAsync(second, ""));

        // Kiểm tra: không lẫn đơn của người khác
        Assert.Equal(1, firstPage.GetProperty("totalCount").GetInt32());
        Assert.Equal(firstOrder, firstPage.GetProperty("items")[0].GetProperty("id").GetGuid());
        Assert.Equal(1, secondPage.GetProperty("totalCount").GetInt32());
        Assert.Equal(secondOrder, secondPage.GetProperty("items")[0].GetProperty("id").GetGuid());
    }

    [SqlFact]
    public async Task History_UnknownSortBy_Returns400()
    {
        // Chuẩn bị
        (HttpClient client, _, _) = await BillingTestData.CreateFreeVaultAsync(factory, null);

        // Hành động
        HttpResponseMessage response = await ListAsync(client, "?sortBy=totalAmount");

        // Kiểm tra
        await ApiTestHelpers.AssertProblemAsync(response, HttpStatusCode.BadRequest, "VALIDATION_ERROR");
    }

    [SqlFact]
    public async Task History_WithoutVault_Returns404()
    {
        // Chuẩn bị: user mới chưa tạo kho
        HttpClient client = await ApiTestHelpers.CreateClientForNewUserAsync(factory, emailVerified: true);

        // Hành động
        HttpResponseMessage response = await ListAsync(client, "");

        // Kiểm tra
        await ApiTestHelpers.AssertProblemAsync(response, HttpStatusCode.NotFound, "VAULT_NOT_FOUND");
    }

    [SqlFact]
    public async Task History_WithoutLogin_Returns401()
    {
        // Chuẩn bị
        HttpClient anonymous = factory.CreateClient();

        // Hành động
        HttpResponseMessage response = await ListAsync(anonymous, "");

        // Kiểm tra
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
