using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LegacyVault.Application.Services;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;
using LegacyVault.IntegrationTests.DeathCases;
using LegacyVault.IntegrationTests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace LegacyVault.IntegrationTests;

/// <summary>Công cụ thử nghiệm: Admin đưa kho về trước bước eKYC và mua gói (chỉ có ở Development).</summary>
public sealed class DevToolsRewindBeforeEkycTests : IAsyncLifetime
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

    [SqlFact]
    public async Task RewindRemovesEkycResetsEntitlementCancelsPendingOrderAndKeepsPaidOrder()
    {
        User admin = await DeathCaseTestData.AddUserAsync(factory, UserRole.Admin, UserStatus.Active);
        DeathCaseTestData.OwnerVault target = await CreatePaidOwnerVaultAsync();
        DeathCaseTestData.OwnerVault other = await CreatePaidOwnerVaultAsync();
        Guid paidOrderId = await AddOrderAsync(target.VaultId, target.Owner.Id, PaymentOrderStatus.Paid);
        Guid pendingOrderId = await AddOrderAsync(target.VaultId, target.Owner.Id, PaymentOrderStatus.Pending);
        await AddEkycDataAsync(target.Owner.Id);
        await AddEkycDataAsync(other.Owner.Id);

        HttpClient client = factory.CreateClient();
        ApiTestHelpers.AuthorizeAs(client, factory.Services, admin);
        HttpResponseMessage response = await client.PostAsync(Url(target.VaultId), null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        JsonElement body = await ApiTestHelpers.ReadJsonAsync(response);
        Assert.Equal(target.VaultId, body.GetProperty("vaultId").GetGuid());
        Assert.Equal(target.Owner.Email, body.GetProperty("ownerEmail").GetString());
        Assert.Equal(1, body.GetProperty("verificationsRemoved").GetInt32());
        Assert.True(body.GetProperty("identityRemoved").GetBoolean());
        Assert.Equal("XS", body.GetProperty("previousTier").GetString());
        Assert.True(body.GetProperty("entitlementReset").GetBoolean());
        Assert.Equal(1, body.GetProperty("pendingOrdersCancelled").GetInt32());

        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            Assert.Equal(0, await db.Set<EkycVerification>().CountAsync(v => v.UserId == target.Owner.Id));
            Assert.Equal(0, await db.Set<OwnerIdentity>().CountAsync(i => i.UserId == target.Owner.Id));

            VaultEntitlement entitlement = await db.Set<VaultEntitlement>().SingleAsync(e => e.VaultId == target.VaultId);
            Assert.Equal(ServiceTier.Free, entitlement.Tier);
            Assert.Equal(EntitlementStatus.Free, entitlement.Status);
            Assert.Equal(StorageLimits.FreeQuotaBytes, entitlement.StorageQuotaBytes);
            Assert.Equal(0, entitlement.IncludedBeneficiarySlots);
            Assert.Equal(0, entitlement.ExtraBeneficiarySlots);
            Assert.Null(entitlement.ServiceExpiresAt);

            PaymentOrder pending = await db.Set<PaymentOrder>().SingleAsync(o => o.Id == pendingOrderId);
            Assert.Equal(PaymentOrderStatus.Cancelled, pending.Status);
            Assert.NotNull(pending.CancelledAt);
            PaymentOrder paid = await db.Set<PaymentOrder>().SingleAsync(o => o.Id == paidOrderId);
            Assert.Equal(PaymentOrderStatus.Paid, paid.Status);

            // Kho khác không bị đụng tới.
            VaultEntitlement otherEntitlement = await db.Set<VaultEntitlement>().SingleAsync(e => e.VaultId == other.VaultId);
            Assert.Equal(ServiceTier.Xs, otherEntitlement.Tier);
            Assert.Equal(1, await db.Set<EkycVerification>().CountAsync(v => v.UserId == other.Owner.Id));

            AuditLog log = await db.Set<AuditLog>().SingleAsync(l => l.Action == AuditAction.DevRewindBeforeEkyc);
            Assert.Equal(admin.Id, log.ActorUserId);
            Assert.Equal(target.VaultId, log.VaultId);
        });
    }

    [SqlFact]
    public async Task VaultAlreadyBeforeEkycReturnsZerosAndWritesNoAudit()
    {
        User admin = await DeathCaseTestData.AddUserAsync(factory, UserRole.Admin, UserStatus.Active);
        User owner = await DeathCaseTestData.AddUserAsync(factory, UserRole.User, UserStatus.Active);
        HttpClient ownerClient = factory.CreateClient();
        ApiTestHelpers.AuthorizeAs(ownerClient, factory.Services, owner);
        Guid vaultId = await ApiTestHelpers.CreateVaultAsync(ownerClient);

        HttpClient client = factory.CreateClient();
        ApiTestHelpers.AuthorizeAs(client, factory.Services, admin);
        HttpResponseMessage response = await client.PostAsync(Url(vaultId), null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        JsonElement body = await ApiTestHelpers.ReadJsonAsync(response);
        Assert.Equal(0, body.GetProperty("verificationsRemoved").GetInt32());
        Assert.False(body.GetProperty("entitlementReset").GetBoolean());
        Assert.Equal(0, body.GetProperty("pendingOrdersCancelled").GetInt32());
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            Assert.False(await db.Set<AuditLog>().AnyAsync(l => l.Action == AuditAction.DevRewindBeforeEkyc));
        });
    }

    [SqlFact]
    public async Task RunningPlanIs409AndChangesNothing()
    {
        User admin = await DeathCaseTestData.AddUserAsync(factory, UserRole.Admin, UserStatus.Active);
        DeathCaseTestData.OwnerVault target = await CreatePaidOwnerVaultAsync();
        await ApiTestHelpers.SetPlanStatusAsync(factory, target.VaultId, PlanStatus.Active);
        await AddEkycDataAsync(target.Owner.Id);

        HttpClient client = factory.CreateClient();
        ApiTestHelpers.AuthorizeAs(client, factory.Services, admin);
        HttpResponseMessage response = await client.PostAsync(Url(target.VaultId), null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        JsonElement body = await ApiTestHelpers.ReadJsonAsync(response);
        Assert.Equal("DEV_TOOL_NOT_ALLOWED", body.GetProperty("code").GetString());
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            Assert.Equal(1, await db.Set<EkycVerification>().CountAsync(v => v.UserId == target.Owner.Id));
            VaultEntitlement entitlement = await db.Set<VaultEntitlement>().SingleAsync(e => e.VaultId == target.VaultId);
            Assert.Equal(ServiceTier.Xs, entitlement.Tier);
            Assert.False(await db.Set<AuditLog>().AnyAsync(l => l.Action == AuditAction.DevRewindBeforeEkyc));
        });
    }

    [SqlFact]
    public async Task UnknownVaultIs404_NonAdminIs404()
    {
        User admin = await DeathCaseTestData.AddUserAsync(factory, UserRole.Admin, UserStatus.Active);
        DeathCaseTestData.OwnerVault target = await CreatePaidOwnerVaultAsync();

        HttpClient adminClient = factory.CreateClient();
        ApiTestHelpers.AuthorizeAs(adminClient, factory.Services, admin);
        HttpResponseMessage unknown = await adminClient.PostAsync(Url(Guid.NewGuid()), null);
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);

        HttpResponseMessage forbidden = await target.Client.PostAsync(Url(target.VaultId), null);
        Assert.Equal(HttpStatusCode.NotFound, forbidden.StatusCode);
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            VaultEntitlement entitlement = await db.Set<VaultEntitlement>().SingleAsync(e => e.VaultId == target.VaultId);
            Assert.Equal(ServiceTier.Xs, entitlement.Tier);
        });
    }

    private static string Url(Guid vaultId)
    {
        return "/api/v1/admin/dev-tools/vaults/" + vaultId + "/rewind-before-ekyc";
    }

    /// <summary>Owner có kho (kế hoạch Nháp) với quyền dịch vụ Xs đã trả.</summary>
    private async Task<DeathCaseTestData.OwnerVault> CreatePaidOwnerVaultAsync()
    {
        User owner = await DeathCaseTestData.AddUserAsync(factory, UserRole.User, UserStatus.Active);
        HttpClient client = factory.CreateClient();
        ApiTestHelpers.AuthorizeAs(client, factory.Services, owner);
        Guid vaultId = await ApiTestHelpers.CreateVaultAsync(client);
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            VaultEntitlement entitlement = await db.Set<VaultEntitlement>().SingleAsync(e => e.VaultId == vaultId);
            entitlement.Tier = ServiceTier.Xs;
            entitlement.Status = EntitlementStatus.PaidActive;
            entitlement.StorageQuotaBytes = 209715200;
            entitlement.IncludedBeneficiarySlots = 2;
            entitlement.ServiceExpiresAt = DateTimeOffset.UtcNow.AddDays(30);
            await db.SaveChangesAsync();
        });

        DeathCaseTestData.OwnerVault result = new DeathCaseTestData.OwnerVault();
        result.Owner = owner;
        result.Client = client;
        result.VaultId = vaultId;
        return result;
    }

    private async Task<Guid> AddOrderAsync(Guid vaultId, Guid ownerId, PaymentOrderStatus status)
    {
        Guid orderId = Guid.CreateVersion7();
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            DateTimeOffset now = DateTimeOffset.UtcNow;
            PaymentOrder order = new PaymentOrder();
            order.Id = orderId;
            order.VaultId = vaultId;
            order.OwnerId = ownerId;
            order.Kind = PaymentOrderKind.Purchase;
            order.Tier = ServiceTier.Xs;
            order.Years = 1;
            order.TotalAmount = 100000;
            order.TierAmount = 100000;
            order.StorageQuotaBytes = 209715200;
            order.IncludedBeneficiarySlots = 2;
            order.PolicyVersion = "test";
            order.PaymentCode = "LV" + orderId.ToString("N").Substring(20).ToUpperInvariant();
            order.Status = status;
            order.ExpiresAt = now.AddHours(1);
            order.CreatedAt = now;
            order.UpdatedAt = now;
            db.Add(order);
            await db.SaveChangesAsync();
        });
        return orderId;
    }

    /// <summary>Một lượt eKYC đã đạt và danh tính giả (dữ liệu mã hóa giả, không có định danh thật).</summary>
    private Task AddEkycDataAsync(Guid userId)
    {
        return ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            DateTimeOffset now = DateTimeOffset.UtcNow;
            EkycVerification verification = new EkycVerification();
            verification.UserId = userId;
            verification.Purpose = EkycPurpose.OwnerEnrollment;
            verification.Status = EkycStatus.Passed;
            verification.IdentityMatch = EkycIdentityMatch.NotChecked;
            verification.CitizenIdLast4 = "1234";
            verification.CreatedAt = now;
            verification.CompletedAt = now;
            db.Add(verification);

            OwnerIdentity identity = new OwnerIdentity();
            identity.UserId = userId;
            identity.CitizenIdLast4 = "1234";
            identity.SourceVerificationId = verification.Id;
            identity.EncryptionKeyId = "test";
            identity.WrappedDataKey = new byte[] { 1 };
            identity.IdentityEncrypted = new byte[] { 2 };
            identity.CreatedAt = now;
            db.Add(identity);

            await db.SaveChangesAsync();
        });
    }
}
