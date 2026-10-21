using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;
using LegacyVault.IntegrationTests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace LegacyVault.IntegrationTests.Payment;

/// <summary>GET /api/v1/billing/plans và POST /api/v1/billing/quote trên API thật và SQL Server tạm (SRS §8.1–8.3).</summary>
public sealed class BillingQuoteFlowTests : IAsyncLifetime
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

    /// <summary>User mới có kho Free, kèm eKYC Owner đạt cách đây ekycDaysAgo ngày (null = chưa có eKYC).</summary>
    private async Task<(HttpClient Client, Guid VaultId, Guid OwnerId)> CreateFreeVaultAsync(int? ekycDaysAgo)
    {
        HttpClient client = await ApiTestHelpers.CreateClientForNewUserAsync(factory, emailVerified: true);
        Guid vaultId = await ApiTestHelpers.CreateVaultAsync(client);
        Guid ownerId = Guid.Empty;
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            Vault vault = await db.Vaults.SingleAsync(v => v.Id == vaultId);
            ownerId = vault.OwnerId;
            if (ekycDaysAgo != null)
            {
                EkycVerification verification = new EkycVerification();
                verification.UserId = ownerId;
                verification.Purpose = EkycPurpose.OwnerEnrollment;
                verification.Status = EkycStatus.Passed;
                verification.CreatedAt = DateTimeOffset.UtcNow.AddDays(-ekycDaysAgo.Value);
                verification.CompletedAt = DateTimeOffset.UtcNow.AddDays(-ekycDaysAgo.Value);
                db.EkycVerifications.Add(verification);
                await db.SaveChangesAsync();
            }
        });
        return (client, vaultId, ownerId);
    }

    /// <summary>Đặt kho thành XS đang trả phí, hết hạn sau daysLeft ngày (sửa thẳng DB, chưa có dev-pay).</summary>
    private async Task MakePaidActiveAsync(Guid vaultId, int daysLeft)
    {
        await ApiTestHelpers.UpgradeVaultToXsAsync(factory, vaultId);
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            DateTimeOffset expiresAt = DateTimeOffset.UtcNow.AddDays(daysLeft);
            await db.VaultEntitlements.Where(e => e.VaultId == vaultId)
                .ExecuteUpdateAsync(s => s.SetProperty(e => e.ServiceExpiresAt, expiresAt));
        });
    }

    private static Task<HttpResponseMessage> QuoteAsync(HttpClient client, object body)
    {
        return client.PostAsJsonAsync("/api/v1/billing/quote", body);
    }

    private async Task<int> CountPaymentOrdersAsync()
    {
        int count = 0;
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            count = await db.PaymentOrders.CountAsync();
        });
        return count;
    }

    [SqlFact]
    public async Task Sec81_GetPlans_ReturnsPriceTable()
    {
        // Chuẩn bị
        HttpClient client = await ApiTestHelpers.CreateClientForNewUserAsync(factory, emailVerified: true);

        // Hành động
        HttpResponseMessage response = await client.GetAsync("/api/v1/billing/plans");

        // Kiểm tra
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        JsonElement plans = await ApiTestHelpers.ReadJsonAsync(response);
        Assert.Equal("2026-10", plans.GetProperty("policyVersion").GetString());
        Assert.Equal("VND", plans.GetProperty("currency").GetString());
        JsonElement tiers = plans.GetProperty("tiers");
        Assert.Equal(2, tiers.GetArrayLength());
        Assert.Equal("XS", tiers[0].GetProperty("tier").GetString());
        Assert.Equal(2, tiers[0].GetProperty("includedBeneficiarySlots").GetInt32());
        Assert.Equal(200 * 1048576L, tiers[0].GetProperty("storageQuotaBytes").GetInt64());
        Assert.Equal(199000, tiers[0].GetProperty("prices")[0].GetProperty("amount").GetInt64());
        Assert.Equal("XS_MAX", tiers[1].GetProperty("tier").GetString());
        Assert.Equal(1599000, tiers[1].GetProperty("prices")[2].GetProperty("amount").GetInt64());
        Assert.Equal(1825, tiers[1].GetProperty("prices")[2].GetProperty("serviceDays").GetInt32());
        Assert.Equal(49000, plans.GetProperty("extraSlot").GetProperty("pricePerYear").GetInt64());
        Assert.Equal(20, plans.GetProperty("extraSlot").GetProperty("maxPerOrder").GetInt32());
    }

    [SqlFact]
    public async Task Sec81_PlansAndQuote_WithoutLogin_Return401()
    {
        // Chuẩn bị
        HttpClient anonymous = factory.CreateClient();

        // Hành động
        HttpResponseMessage plans = await anonymous.GetAsync("/api/v1/billing/plans");
        HttpResponseMessage quote = await QuoteAsync(anonymous, new { tier = "XS", years = 1, extraSlots = 0 });

        // Kiểm tra
        Assert.Equal(HttpStatusCode.Unauthorized, plans.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, quote.StatusCode);
    }

    [SqlFact]
    public async Task Ac94_Quote_PurchaseXsThreeYearsOneSlot_Is646000_AndWritesNothing()
    {
        // Chuẩn bị
        (HttpClient client, Guid vaultId, Guid ownerId) = await CreateFreeVaultAsync(ekycDaysAgo: 1);

        // Hành động: body cố ý kèm số tiền và loại đơn giả, backend phải bỏ qua (PAY-11)
        HttpResponseMessage response = await QuoteAsync(client,
            new { tier = "XS", years = 3, extraSlots = 1, totalAmount = 1, kind = "ADDON", status = "PAID" });

        // Kiểm tra
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        JsonElement quote = await ApiTestHelpers.ReadJsonAsync(response);
        Assert.Equal("PURCHASE", quote.GetProperty("kind").GetString());
        Assert.Equal("XS", quote.GetProperty("tier").GetString());
        Assert.Equal(3, quote.GetProperty("years").GetInt32());
        Assert.Equal(499000, quote.GetProperty("tierAmount").GetInt64());
        Assert.Equal(147000, quote.GetProperty("slotAmount").GetInt64());
        Assert.Equal(646000, quote.GetProperty("totalAmount").GetInt64());
        Assert.Equal("VND", quote.GetProperty("currency").GetString());
        Assert.Equal(3, quote.GetProperty("beneficiarySlots").GetInt32());
        Assert.Equal(1095, quote.GetProperty("serviceDays").GetInt32());
        Assert.Equal(200 * 1048576L, quote.GetProperty("storageQuotaBytes").GetInt64());
        Assert.Equal("ON_PAYMENT", quote.GetProperty("serviceStartsAt").GetString());
        Assert.Equal("2026-10", quote.GetProperty("policyVersion").GetString());
        Assert.Equal("Thanh toán không tự kích hoạt bàn giao.", quote.GetProperty("notice").GetString());
        Assert.Equal(0, await CountPaymentOrdersAsync());
    }

    [SqlFact]
    public async Task Ac94_Quote_PurchaseXsMaxFiveYears_Is1599000()
    {
        // Chuẩn bị
        (HttpClient client, _, _) = await CreateFreeVaultAsync(ekycDaysAgo: 1);

        // Hành động
        HttpResponseMessage response = await QuoteAsync(client, new { tier = "XS_MAX", years = 5, extraSlots = 0 });

        // Kiểm tra
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        JsonElement quote = await ApiTestHelpers.ReadJsonAsync(response);
        Assert.Equal(1599000, quote.GetProperty("totalAmount").GetInt64());
        Assert.Equal(5, quote.GetProperty("beneficiarySlots").GetInt32());
        Assert.Equal(1825, quote.GetProperty("serviceDays").GetInt32());
    }

    [SqlFact]
    public async Task Sec83_Quote_FreeVaultWithoutEkyc_OwnerEkycRequired()
    {
        // Chuẩn bị
        (HttpClient client, _, _) = await CreateFreeVaultAsync(ekycDaysAgo: null);

        // Hành động
        HttpResponseMessage response = await QuoteAsync(client, new { tier = "XS", years = 1, extraSlots = 0 });

        // Kiểm tra
        JsonElement problem = await ApiTestHelpers.AssertProblemAsync(response, HttpStatusCode.Conflict, "PAYMENT_NOT_ALLOWED");
        Assert.Equal("OWNER_EKYC_REQUIRED", problem.GetProperty("reason").GetString());
    }

    [SqlFact]
    public async Task Sec83_Quote_EkycOlderThanNinetyDays_OwnerEkycRequired()
    {
        // Chuẩn bị
        (HttpClient client, _, _) = await CreateFreeVaultAsync(ekycDaysAgo: 91);

        // Hành động
        HttpResponseMessage response = await QuoteAsync(client, new { tier = "XS", years = 1, extraSlots = 0 });

        // Kiểm tra
        JsonElement problem = await ApiTestHelpers.AssertProblemAsync(response, HttpStatusCode.Conflict, "PAYMENT_NOT_ALLOWED");
        Assert.Equal("OWNER_EKYC_REQUIRED", problem.GetProperty("reason").GetString());
    }

    [SqlFact]
    public async Task Sec83_Quote_EkycFailed_OwnerEkycRequired()
    {
        // Chuẩn bị: lượt eKYC mới nhất của Owner không đạt
        (HttpClient client, _, Guid ownerId) = await CreateFreeVaultAsync(ekycDaysAgo: null);
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            EkycVerification verification = new EkycVerification();
            verification.UserId = ownerId;
            verification.Purpose = EkycPurpose.OwnerEnrollment;
            verification.Status = EkycStatus.Failed;
            verification.CreatedAt = DateTimeOffset.UtcNow;
            verification.CompletedAt = DateTimeOffset.UtcNow;
            db.EkycVerifications.Add(verification);
            await db.SaveChangesAsync();
        });

        // Hành động
        HttpResponseMessage response = await QuoteAsync(client, new { tier = "XS", years = 1, extraSlots = 0 });

        // Kiểm tra
        JsonElement problem = await ApiTestHelpers.AssertProblemAsync(response, HttpStatusCode.Conflict, "PAYMENT_NOT_ALLOWED");
        Assert.Equal("OWNER_EKYC_REQUIRED", problem.GetProperty("reason").GetString());
    }

    [SqlFact]
    public async Task Sec83_Quote_EkycForOtherPurpose_DoesNotCount()
    {
        // Chuẩn bị: eKYC đạt nhưng mục đích BeneficiaryClaim (EKYC-01)
        (HttpClient client, _, Guid ownerId) = await CreateFreeVaultAsync(ekycDaysAgo: null);
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            EkycVerification verification = new EkycVerification();
            verification.UserId = ownerId;
            verification.Purpose = EkycPurpose.BeneficiaryClaim;
            verification.Status = EkycStatus.Passed;
            verification.CreatedAt = DateTimeOffset.UtcNow;
            verification.CompletedAt = DateTimeOffset.UtcNow;
            db.EkycVerifications.Add(verification);
            await db.SaveChangesAsync();
        });

        // Hành động
        HttpResponseMessage response = await QuoteAsync(client, new { tier = "XS", years = 1, extraSlots = 0 });

        // Kiểm tra
        JsonElement problem = await ApiTestHelpers.AssertProblemAsync(response, HttpStatusCode.Conflict, "PAYMENT_NOT_ALLOWED");
        Assert.Equal("OWNER_EKYC_REQUIRED", problem.GetProperty("reason").GetString());
    }

    [SqlFact]
    public async Task Sec83_Quote_PlanLocked_ReturnsPlanLocked()
    {
        PlanStatus[] lockedStatuses = new PlanStatus[]
        {
            PlanStatus.DeathCaseOpen, PlanStatus.HandoverInProgress, PlanStatus.Blocked,
        };
        foreach (PlanStatus status in lockedStatuses)
        {
            // Chuẩn bị
            (HttpClient client, Guid vaultId, _) = await CreateFreeVaultAsync(ekycDaysAgo: 1);
            await ApiTestHelpers.SetPlanStatusAsync(factory, vaultId, status);

            // Hành động
            HttpResponseMessage response = await QuoteAsync(client, new { tier = "XS", years = 1, extraSlots = 0 });

            // Kiểm tra
            JsonElement problem = await ApiTestHelpers.AssertProblemAsync(response, HttpStatusCode.Conflict, "PAYMENT_NOT_ALLOWED");
            Assert.Equal("PLAN_LOCKED", problem.GetProperty("reason").GetString());
        }
    }

    [SqlFact]
    public async Task Ac104_Quote_ClosedVault_ReturnsVaultClosed()
    {
        // Chuẩn bị
        (HttpClient client, Guid vaultId, _) = await CreateFreeVaultAsync(ekycDaysAgo: 1);
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            await db.Vaults.Where(v => v.Id == vaultId)
                .ExecuteUpdateAsync(s => s.SetProperty(v => v.Status, VaultStatus.Closed));
        });

        // Hành động
        HttpResponseMessage response = await QuoteAsync(client, new { tier = "XS", years = 1, extraSlots = 0 });

        // Kiểm tra
        JsonElement problem = await ApiTestHelpers.AssertProblemAsync(response, HttpStatusCode.Conflict, "PAYMENT_NOT_ALLOWED");
        Assert.Equal("VAULT_CLOSED", problem.GetProperty("reason").GetString());
    }

    [SqlFact]
    public async Task Sec83_Quote_GraceReadOnly_RenewalNotSupported()
    {
        // Chuẩn bị
        (HttpClient client, Guid vaultId, _) = await CreateFreeVaultAsync(ekycDaysAgo: 1);
        await MakePaidActiveAsync(vaultId, daysLeft: 30);
        await ApiTestHelpers.SetEntitlementStatusAsync(factory, vaultId, EntitlementStatus.GraceReadOnly);

        // Hành động
        HttpResponseMessage response = await QuoteAsync(client, new { tier = "XS", years = 1, extraSlots = 0 });

        // Kiểm tra
        JsonElement problem = await ApiTestHelpers.AssertProblemAsync(response, HttpStatusCode.Conflict, "PAYMENT_NOT_ALLOWED");
        Assert.Equal("RENEWAL_NOT_SUPPORTED", problem.GetProperty("reason").GetString());
    }

    [SqlFact]
    public async Task Sec83_Quote_PaidActiveButExpired_RenewalNotSupported()
    {
        // Chuẩn bị: còn PaidActive nhưng đã quá hạn (job chuyển ân hạn chưa chạy)
        (HttpClient client, Guid vaultId, _) = await CreateFreeVaultAsync(ekycDaysAgo: 1);
        await MakePaidActiveAsync(vaultId, daysLeft: -1);

        // Hành động
        HttpResponseMessage response = await QuoteAsync(client, new { extraSlots = 1 });

        // Kiểm tra
        JsonElement problem = await ApiTestHelpers.AssertProblemAsync(response, HttpStatusCode.Conflict, "PAYMENT_NOT_ALLOWED");
        Assert.Equal("RENEWAL_NOT_SUPPORTED", problem.GetProperty("reason").GetString());
    }

    [SqlFact]
    public async Task Ac95_Quote_AddonWith180DaysLeft_Is24165_IgnoresTierAndYears()
    {
        // Chuẩn bị: XS còn 180 ngày; không cần eKYC cho ADDON
        (HttpClient client, Guid vaultId, _) = await CreateFreeVaultAsync(ekycDaysAgo: null);
        await MakePaidActiveAsync(vaultId, daysLeft: 180);

        // Hành động: tier và years gửi vào bị bỏ qua
        HttpResponseMessage response = await QuoteAsync(client, new { tier = "XS_MAX", years = 5, extraSlots = 1 });

        // Kiểm tra
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        JsonElement quote = await ApiTestHelpers.ReadJsonAsync(response);
        Assert.Equal("ADDON", quote.GetProperty("kind").GetString());
        Assert.Equal("XS", quote.GetProperty("tier").GetString());
        Assert.Equal(JsonValueKind.Null, quote.GetProperty("years").ValueKind);
        Assert.Equal(0, quote.GetProperty("tierAmount").GetInt64());
        Assert.Equal(24165, quote.GetProperty("totalAmount").GetInt64());
        Assert.Equal(3, quote.GetProperty("beneficiarySlots").GetInt32());
        Assert.Equal(JsonValueKind.Null, quote.GetProperty("serviceStartsAt").ValueKind);
        Assert.NotEqual(JsonValueKind.Null, quote.GetProperty("serviceExpiresAt").ValueKind);
        Assert.Equal(0, await CountPaymentOrdersAsync());
    }

    [SqlFact]
    public async Task Addon01_Quote_PurchaseExtraSlotsTwentyOne_Returns400()
    {
        // Chuẩn bị
        (HttpClient client, _, _) = await CreateFreeVaultAsync(ekycDaysAgo: 1);

        // Hành động
        HttpResponseMessage response = await QuoteAsync(client, new { tier = "XS", years = 1, extraSlots = 21 });

        // Kiểm tra
        JsonElement problem = await ApiTestHelpers.AssertProblemAsync(response, HttpStatusCode.BadRequest, "VALIDATION_ERROR");
        Assert.True(problem.GetProperty("errors").TryGetProperty("extraSlots", out _));
    }

    [SqlFact]
    public async Task Sec81_Quote_YearsTwo_Returns400()
    {
        // Chuẩn bị
        (HttpClient client, _, _) = await CreateFreeVaultAsync(ekycDaysAgo: 1);

        // Hành động
        HttpResponseMessage response = await QuoteAsync(client, new { tier = "XS", years = 2, extraSlots = 0 });

        // Kiểm tra
        JsonElement problem = await ApiTestHelpers.AssertProblemAsync(response, HttpStatusCode.BadRequest, "VALIDATION_ERROR");
        Assert.True(problem.GetProperty("errors").TryGetProperty("years", out _));
    }

    [SqlFact]
    public async Task Sec81_Quote_TierFree_Returns400()
    {
        // Chuẩn bị
        (HttpClient client, _, _) = await CreateFreeVaultAsync(ekycDaysAgo: 1);

        // Hành động
        HttpResponseMessage response = await QuoteAsync(client, new { tier = "FREE", years = 1, extraSlots = 0 });

        // Kiểm tra
        JsonElement problem = await ApiTestHelpers.AssertProblemAsync(response, HttpStatusCode.BadRequest, "VALIDATION_ERROR");
        Assert.True(problem.GetProperty("errors").TryGetProperty("tier", out _));
    }

    [SqlFact]
    public async Task Addon01_Quote_AddonWithZeroSlots_Returns400()
    {
        // Chuẩn bị
        (HttpClient client, Guid vaultId, _) = await CreateFreeVaultAsync(ekycDaysAgo: null);
        await MakePaidActiveAsync(vaultId, daysLeft: 100);

        // Hành động
        HttpResponseMessage response = await QuoteAsync(client, new { extraSlots = 0 });

        // Kiểm tra
        JsonElement problem = await ApiTestHelpers.AssertProblemAsync(response, HttpStatusCode.BadRequest, "VALIDATION_ERROR");
        Assert.True(problem.GetProperty("errors").TryGetProperty("extraSlots", out _));
    }

    [SqlFact]
    public async Task Setup12_Quote_UserWithoutVault_Returns404()
    {
        // Chuẩn bị: user chưa tạo kho
        HttpClient client = await ApiTestHelpers.CreateClientForNewUserAsync(factory, emailVerified: true);

        // Hành động
        HttpResponseMessage response = await QuoteAsync(client, new { tier = "XS", years = 1, extraSlots = 0 });

        // Kiểm tra
        await ApiTestHelpers.AssertProblemAsync(response, HttpStatusCode.NotFound, "VAULT_NOT_FOUND");
    }
}
