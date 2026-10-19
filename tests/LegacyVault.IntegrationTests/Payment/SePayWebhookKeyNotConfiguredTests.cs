using System.Net;
using System.Net.Http.Json;
using LegacyVault.Application.Services;
using LegacyVault.IntegrationTests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace LegacyVault.IntegrationTests.Payment;

/// <summary>Server chưa cấu hình key webhook thì không bao giờ nhận giao dịch (SRS §8.5, PAY-01).</summary>
public sealed class SePayWebhookKeyNotConfiguredTests : IAsyncLifetime
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
        factory.ConfigureAdditionalServices = services =>
        {
            services.RemoveAll<IBillingSePaySettings>();
            services.AddSingleton<IBillingSePaySettings>(new EmptyKeySettings());
        };
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

    [SqlFact]
    public async Task Pay05_Webhook_ServerKeyNotConfigured_RejectsAnyKeyAndSavesNothing()
    {
        // Chuẩn bị: kẻ gửi thử key rỗng và key đoán
        HttpClient client = factory.CreateClient();
        Dictionary<string, object?> payload = new Dictionary<string, object?>();
        payload["id"] = 1;
        payload["accountNumber"] = "0000000000";
        payload["transferType"] = "in";
        payload["transferAmount"] = 199000;
        List<string> headers = new List<string> { "Apikey ", "Apikey guess", "Apikey " + ApiFactory.SePayTestApiKey };

        // Hành động + Kiểm tra từng ca
        foreach (string header in headers)
        {
            HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/billing/sepay/webhook");
            request.Content = JsonContent.Create(payload);
            request.Headers.TryAddWithoutValidation("Authorization", header);
            HttpResponseMessage response = await client.SendAsync(request);
            await ApiTestHelpers.AssertProblemAsync(response, HttpStatusCode.Unauthorized, "WEBHOOK_UNAUTHORIZED");
        }

        int count = 0;
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            count = await db.PaymentTransactions.CountAsync();
        });
        Assert.Equal(0, count);
    }

    private sealed class EmptyKeySettings : IBillingSePaySettings
    {
        public string WebhookApiKey
        {
            get { return string.Empty; }
        }

        public string Environment
        {
            get { return "Test"; }
        }
    }
}
