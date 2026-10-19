using System.Net;
using System.Net.Http.Json;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;
using LegacyVault.IntegrationTests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace LegacyVault.IntegrationTests.Payment;

/// <summary>
/// Webhook SePay bước nhận và lưu (SRS §8.3 bước 6, §8.5, PAY-01, PAY-05, AC-29): xác thực key, kiểm dữ liệu,
/// chống trùng, lưu trạng thái Received. Chạy API thật trên SQL Server database tạm.
/// </summary>
public sealed class SePayWebhookStoreTests : IAsyncLifetime
{
    private const string WebhookUrl = "/api/v1/billing/sepay/webhook";

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

    /// <summary>Payload hợp lệ của SePay (số tài khoản và nội dung giả).</summary>
    private static Dictionary<string, object?> ValidPayload(long id)
    {
        Dictionary<string, object?> payload = new Dictionary<string, object?>();
        payload["id"] = id;
        payload["gateway"] = "MBBank";
        payload["transactionDate"] = "2026-10-19 14:30:15";
        payload["accountNumber"] = "0000000000";
        payload["subAccount"] = null;
        payload["code"] = null;
        payload["content"] = "chuyen tien LVK7M2P9QX cam on";
        payload["transferType"] = "in";
        payload["description"] = "BankAPINotify chuyen tien";
        payload["transferAmount"] = 199000;
        payload["accumulated"] = 0;
        payload["referenceCode"] = "FT26289000001";
        return payload;
    }

    private static HttpRequestMessage NewRequest(Dictionary<string, object?> payload, string? authorization)
    {
        HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, WebhookUrl);
        request.Content = JsonContent.Create(payload);
        if (authorization != null)
        {
            request.Headers.TryAddWithoutValidation("Authorization", authorization);
        }
        return request;
    }

    private static string ValidAuthorization()
    {
        return "Apikey " + ApiFactory.SePayTestApiKey;
    }

    private async Task<HttpResponseMessage> SendAsync(Dictionary<string, object?> payload, string? authorization)
    {
        // Client không đăng nhập: SePay không có JWT của người dùng.
        HttpClient client = factory.CreateClient();
        HttpRequestMessage request = NewRequest(payload, authorization);
        return await client.SendAsync(request);
    }

    private async Task<int> CountTransactionsAsync()
    {
        int count = 0;
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            count = await db.PaymentTransactions.CountAsync();
        });
        return count;
    }

    private async Task<PaymentTransaction> LoadSingleTransactionAsync(long providerTransactionId)
    {
        PaymentTransaction? loaded = null;
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            loaded = await db.PaymentTransactions.SingleAsync(t => t.ProviderTransactionId == providerTransactionId);
        });
        return loaded!;
    }

    [SqlFact]
    public async Task Pay05_Webhook_NoAuthorizationHeader_Returns401AndSavesNothing()
    {
        // Chuẩn bị
        Dictionary<string, object?> payload = ValidPayload(1001);

        // Hành động
        HttpResponseMessage response = await SendAsync(payload, null);

        // Kiểm tra
        await ApiTestHelpers.AssertProblemAsync(response, HttpStatusCode.Unauthorized, "WEBHOOK_UNAUTHORIZED");
        Assert.Equal(0, await CountTransactionsAsync());
    }

    [SqlFact]
    public async Task Pay05_Webhook_WrongKey_Returns401AndSavesNothing()
    {
        // Chuẩn bị
        Dictionary<string, object?> payload = ValidPayload(1002);

        // Hành động
        HttpResponseMessage response = await SendAsync(payload, "Apikey sai-key");

        // Kiểm tra
        await ApiTestHelpers.AssertProblemAsync(response, HttpStatusCode.Unauthorized, "WEBHOOK_UNAUTHORIZED");
        Assert.Equal(0, await CountTransactionsAsync());
    }

    [SqlFact]
    public async Task Pay05_Webhook_UserBearerTokenIsNotAccepted_Returns401()
    {
        // Chuẩn bị: JWT của người dùng thật, đúng định dạng Bearer, không thay được key của SePay
        HttpClient userClient = await ApiTestHelpers.CreateClientForNewUserAsync(factory, emailVerified: true);
        string bearer = userClient.DefaultRequestHeaders.Authorization!.ToString();
        HttpRequestMessage request = NewRequest(ValidPayload(1003), bearer);

        // Hành động
        HttpResponseMessage response = await factory.CreateClient().SendAsync(request);

        // Kiểm tra
        await ApiTestHelpers.AssertProblemAsync(response, HttpStatusCode.Unauthorized, "WEBHOOK_UNAUTHORIZED");
        Assert.Equal(0, await CountTransactionsAsync());
    }

    [SqlFact]
    public async Task Pay05_Webhook_ValidKey_SavesReceivedTransactionAndReturnsExactSuccessBody()
    {
        // Chuẩn bị
        Dictionary<string, object?> payload = ValidPayload(2001);

        // Hành động
        HttpResponseMessage response = await SendAsync(payload, ValidAuthorization());

        // Kiểm tra: SePay chỉ coi là thành công khi body đúng {"success":true}
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        string body = await response.Content.ReadAsStringAsync();
        Assert.Equal("{\"success\":true}", body);

        PaymentTransaction saved = await LoadSingleTransactionAsync(2001);
        Assert.Equal("SePay", saved.Provider);
        Assert.Equal("Test", saved.Environment);
        Assert.Equal(PaymentTransactionStatus.Received, saved.Status);
        Assert.Equal("MBBank", saved.Gateway);
        Assert.Equal("0000000000", saved.AccountNumber);
        Assert.Equal("in", saved.TransferType);
        Assert.Equal(199000, saved.TransferAmount);
        Assert.Equal("FT26289000001", saved.ReferenceCode);
        Assert.Equal("LVK7M2P9QX", saved.MatchedPaymentCode);
        Assert.Null(saved.PaymentOrderId);
        Assert.Null(saved.ProcessedAt);
        Assert.Equal(new DateTimeOffset(2026, 10, 19, 7, 30, 15, TimeSpan.Zero), saved.TransactionAt);
    }

    [SqlFact]
    public async Task Pay05_Ac29_Webhook_SameIdSentTwice_Returns200AndKeepsOneRow()
    {
        // Chuẩn bị
        Dictionary<string, object?> firstPayload = ValidPayload(3001);
        HttpResponseMessage firstResponse = await SendAsync(firstPayload, ValidAuthorization());

        // Hành động: SePay gửi lại cùng ID (ví dụ không nhận được phản hồi lần trước); lần này số tiền khác
        Dictionary<string, object?> secondPayload = ValidPayload(3001);
        secondPayload["transferAmount"] = 1;
        HttpResponseMessage secondResponse = await SendAsync(secondPayload, ValidAuthorization());

        // Kiểm tra: vẫn thành công, không thêm dòng, không ghi đè dữ liệu lần đầu
        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, secondResponse.StatusCode);
        Assert.Equal("{\"success\":true}", await secondResponse.Content.ReadAsStringAsync());
        Assert.Equal(1, await CountTransactionsAsync());
        PaymentTransaction saved = await LoadSingleTransactionAsync(3001);
        Assert.Equal(199000, saved.TransferAmount);
    }

    [SqlFact]
    public async Task Pay05_Ac29_Webhook_SameIdSentInParallel_AllReturn200AndKeepOneRow()
    {
        // Chuẩn bị
        List<Task<HttpResponseMessage>> requests = new List<Task<HttpResponseMessage>>();

        // Hành động: năm request cùng ID giao dịch chạy song song
        for (int i = 0; i < 5; i++)
        {
            requests.Add(SendAsync(ValidPayload(4001), ValidAuthorization()));
        }
        HttpResponseMessage[] responses = await Task.WhenAll(requests);

        // Kiểm tra: unique (Provider, ProviderTransactionId) giữ đúng một dòng, request thua không trả lỗi
        foreach (HttpResponseMessage response in responses)
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
        Assert.Equal(1, await CountTransactionsAsync());
    }

    [SqlFact]
    public async Task Pay05_Webhook_InvalidPayloads_Return400AndSaveNothing()
    {
        // Chuẩn bị
        Dictionary<string, object?> missingId = ValidPayload(5001);
        missingId.Remove("id");
        Dictionary<string, object?> zeroId = ValidPayload(0);
        Dictionary<string, object?> missingAmount = ValidPayload(5002);
        missingAmount.Remove("transferAmount");
        Dictionary<string, object?> negativeAmount = ValidPayload(5003);
        negativeAmount["transferAmount"] = -1;
        Dictionary<string, object?> missingAccount = ValidPayload(5004);
        missingAccount.Remove("accountNumber");
        Dictionary<string, object?> longAccount = ValidPayload(5005);
        longAccount["accountNumber"] = new string('9', 33);
        Dictionary<string, object?> missingType = ValidPayload(5006);
        missingType.Remove("transferType");
        Dictionary<string, object?> unknownType = ValidPayload(5007);
        unknownType["transferType"] = "refund";
        List<Dictionary<string, object?>> badPayloads = new List<Dictionary<string, object?>>
        {
            missingId, zeroId, missingAmount, negativeAmount, missingAccount, longAccount, missingType, unknownType,
        };

        // Hành động + Kiểm tra từng ca
        foreach (Dictionary<string, object?> payload in badPayloads)
        {
            HttpResponseMessage response = await SendAsync(payload, ValidAuthorization());
            await ApiTestHelpers.AssertProblemAsync(response, HttpStatusCode.BadRequest, "VALIDATION_ERROR");
        }
        Assert.Equal(0, await CountTransactionsAsync());
    }

    [SqlFact]
    public async Task Pay05_Webhook_InvalidKeyBeatsInvalidPayload_Returns401()
    {
        // Chuẩn bị: vừa sai key vừa sai dữ liệu thì phải xác thực trước (không lộ lỗi dữ liệu cho người lạ)
        Dictionary<string, object?> payload = ValidPayload(6001);
        payload.Remove("transferAmount");

        // Hành động
        HttpResponseMessage response = await SendAsync(payload, "Apikey sai-key");

        // Kiểm tra
        await ApiTestHelpers.AssertProblemAsync(response, HttpStatusCode.Unauthorized, "WEBHOOK_UNAUTHORIZED");
    }

    [SqlFact]
    public async Task Pay05_Webhook_OutgoingTransferIsStoredAsReceived()
    {
        // Chuẩn bị: tiền ra vẫn được lưu để đối chiếu sau (NOT_INCOMING); loại giao dịch chuẩn hóa chữ thường
        Dictionary<string, object?> payload = ValidPayload(7001);
        payload["transferType"] = "OUT";

        // Hành động
        HttpResponseMessage response = await SendAsync(payload, ValidAuthorization());

        // Kiểm tra
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        PaymentTransaction saved = await LoadSingleTransactionAsync(7001);
        Assert.Equal("out", saved.TransferType);
        Assert.Equal(PaymentTransactionStatus.Received, saved.Status);
    }

    [SqlFact]
    public async Task Pay06_Webhook_UnparseableTransactionDate_StoresNullTransactionAt()
    {
        // Chuẩn bị
        Dictionary<string, object?> payload = ValidPayload(8001);
        payload["transactionDate"] = "hôm qua";

        // Hành động
        HttpResponseMessage response = await SendAsync(payload, ValidAuthorization());

        // Kiểm tra: vẫn lưu giao dịch, giờ tiền vào để trống (bước đối chiếu sẽ không tự cấp)
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        PaymentTransaction saved = await LoadSingleTransactionAsync(8001);
        Assert.Null(saved.TransactionAt);
    }

    [SqlFact]
    public async Task Pay05_Webhook_LongContentAndCode_AreTruncatedNotRejected()
    {
        // Chuẩn bị: nội dung dài hơn cột 512 và code dài hơn cột 32; mã đơn nằm ở đầu nội dung
        Dictionary<string, object?> payload = ValidPayload(9001);
        payload["content"] = "LVK7M2P9QX " + new string('x', 600);
        payload["code"] = new string('c', 40);

        // Hành động
        HttpResponseMessage response = await SendAsync(payload, ValidAuthorization());

        // Kiểm tra: lưu được (không 500), mã đơn tìm được từ nội dung vì code quá dài không dùng được
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        PaymentTransaction saved = await LoadSingleTransactionAsync(9001);
        Assert.Equal(512, saved.Content!.Length);
        Assert.Equal(32, saved.Code!.Length);
        Assert.Equal("LVK7M2P9QX", saved.MatchedPaymentCode);
    }

    [SqlFact]
    public async Task Pay05_Webhook_UnknownExtraFieldsFromSePay_AreIgnored()
    {
        // Chuẩn bị: SePay gửi thêm trường mà backend không dùng
        Dictionary<string, object?> payload = ValidPayload(9101);
        payload["someFutureField"] = new { nested = 1 };

        // Hành động
        HttpResponseMessage response = await SendAsync(payload, ValidAuthorization());

        // Kiểm tra
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, await CountTransactionsAsync());
    }
}
