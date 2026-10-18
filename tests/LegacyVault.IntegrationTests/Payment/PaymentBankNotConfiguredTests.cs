using System.Net;
using System.Net.Http.Json;
using LegacyVault.Application.Services;
using LegacyVault.IntegrationTests.TestSupport;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace LegacyVault.IntegrationTests.Payment;

/// <summary>Chưa cấu hình tài khoản nhận tiền thì không phát đơn thanh toán (Owner không có nơi để trả).</summary>
public sealed class PaymentBankNotConfiguredTests : IAsyncLifetime
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
            services.RemoveAll<IBillingBankSettings>();
            services.AddSingleton<IBillingBankSettings>(new EmptyBankSettings());
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
    public async Task Sec83_CreateOrder_BankNotConfigured_Returns500AndWritesNothing()
    {
        // Chuẩn bị
        (HttpClient client, Guid vaultId, _) = await BillingTestData.CreateFreeVaultAsync(factory, 1);

        // Hành động
        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/v1/billing/orders", new { tier = "XS", years = 1, extraSlots = 0 });

        // Kiểm tra
        await ApiTestHelpers.AssertProblemAsync(response, HttpStatusCode.InternalServerError, "INTERNAL_ERROR");
        Assert.Equal(0, await BillingTestData.CountOrdersAsync(factory, vaultId, null));
    }

    private sealed class EmptyBankSettings : IBillingBankSettings
    {
        public string BankCode
        {
            get { return string.Empty; }
        }

        public string AccountNumber
        {
            get { return string.Empty; }
        }

        public string AccountName
        {
            get { return string.Empty; }
        }
    }
}
