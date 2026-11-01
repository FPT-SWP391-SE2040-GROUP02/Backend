using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LegacyVault.Application.Services;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;
using LegacyVault.IntegrationTests.DeathCases;
using LegacyVault.IntegrationTests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace LegacyVault.IntegrationTests.Payment;

/// <summary>
/// Hết hạn dịch vụ (SRS OPLAN-01): kho trả phí hết kỳ thì job chuyển sang ân hạn chỉ đọc (GRACE_READ_ONLY),
/// ghi nhật ký, chặn thao tác thêm; công cụ tua thời gian của Admin tua được mốc này.
/// </summary>
public sealed class EntitlementExpiryTests : IAsyncLifetime
{
    private static readonly DateTimeOffset Now = new DateTimeOffset(2026, 10, 31, 8, 0, 0, TimeSpan.Zero);

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

    private static VaultEntitlement NewEntitlement(EntitlementStatus status, DateTimeOffset? expiresAt)
    {
        VaultEntitlement entitlement = new VaultEntitlement();
        entitlement.Tier = ServiceTier.Xs;
        entitlement.Status = status;
        entitlement.ServiceExpiresAt = expiresAt;
        return entitlement;
    }

    [Fact]
    public void Oplan01_ShouldStartGrace_PaidActiveAtOrPastExpiry_IsTrue()
    {
        // Chuẩn bị
        VaultEntitlement atExpiry = NewEntitlement(EntitlementStatus.PaidActive, Now);
        VaultEntitlement pastExpiry = NewEntitlement(EntitlementStatus.PaidActive, Now.AddDays(-1));

        // Hành động + Kiểm tra
        Assert.True(EntitlementExpiryRules.ShouldStartGrace(atExpiry, Now));
        Assert.True(EntitlementExpiryRules.ShouldStartGrace(pastExpiry, Now));
    }

    [Fact]
    public void Oplan01_ShouldStartGrace_NotYetExpiredFreeOrAlreadyGrace_IsFalse()
    {
        // Chuẩn bị
        VaultEntitlement stillActive = NewEntitlement(EntitlementStatus.PaidActive, Now.AddSeconds(1));
        VaultEntitlement free = NewEntitlement(EntitlementStatus.Free, null);
        VaultEntitlement inGrace = NewEntitlement(EntitlementStatus.GraceReadOnly, Now.AddDays(-10));
        VaultEntitlement noExpiry = NewEntitlement(EntitlementStatus.PaidActive, null);

        // Hành động + Kiểm tra
        Assert.False(EntitlementExpiryRules.ShouldStartGrace(stillActive, Now));
        Assert.False(EntitlementExpiryRules.ShouldStartGrace(free, Now));
        Assert.False(EntitlementExpiryRules.ShouldStartGrace(inGrace, Now));
        Assert.False(EntitlementExpiryRules.ShouldStartGrace(noExpiry, Now));
    }

    [Fact]
    public void Oplan01_StartGrace_KeepsExpiryAsGraceStart()
    {
        // Chuẩn bị
        DateTimeOffset expiry = Now.AddDays(-1);
        VaultEntitlement entitlement = NewEntitlement(EntitlementStatus.PaidActive, expiry);

        // Hành động
        EntitlementExpiryRules.StartGrace(entitlement, Now);

        // Kiểm tra
        Assert.Equal(EntitlementStatus.GraceReadOnly, entitlement.Status);
        Assert.Equal(expiry, entitlement.ServiceExpiresAt);
        Assert.Equal(Now, entitlement.UpdatedAt);
    }

    [SqlFact]
    public async Task Oplan01_Job_ExpiredPaidVault_MovesToGrace_WritesOneAuditLog_AndBlocksAdd()
    {
        // Chuẩn bị: kho XS hết hạn hôm qua
        (HttpClient client, Guid vaultId) = await ApiTestHelpers.CreateClientWithPaidVaultAsync(factory);
        await SetServiceExpiresAtAsync(vaultId, DateTimeOffset.UtcNow.AddDays(-1));

        // Hành động: job quét rồi xử lý, chạy hai lần như khi job nền chạy lặp
        IReadOnlyList<Guid> found = await FindExpiredAsync();
        bool first = await ProcessAsync(vaultId);
        bool second = await ProcessAsync(vaultId);

        // Kiểm tra: chuyển đúng một lần, ghi đúng một dòng nhật ký hệ thống
        Assert.Contains(vaultId, found);
        Assert.True(first);
        Assert.False(second);
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            VaultEntitlement entitlement = await db.VaultEntitlements.SingleAsync(e => e.VaultId == vaultId);
            Assert.Equal(EntitlementStatus.GraceReadOnly, entitlement.Status);
            List<AuditLog> logs = await db.Set<AuditLog>()
                .Where(l => l.VaultId == vaultId && l.Action == AuditAction.ServiceGraceStarted)
                .ToListAsync();
            AuditLog log = Assert.Single(logs);
            Assert.Equal(AuditActorRole.System, log.ActorRole);
            Assert.Null(log.ActorUserId);
        });
        Assert.Empty(await FindExpiredAsync());

        // Kiểm tra: thao tác "thêm" bị chặn theo luật chỉ đọc đã có (OPLAN-01)
        HttpResponseMessage create = await client.PostAsJsonAsync($"/api/v1/vaults/{vaultId}/containers", new { name = "Gói mới" });
        JsonElement problem = await ApiTestHelpers.AssertProblemAsync(create, HttpStatusCode.Conflict, "VAULT_NOT_EDITABLE");
        Assert.Equal("SERVICE_READ_ONLY", problem.GetProperty("reason").GetString());
    }

    [SqlFact]
    public async Task Oplan01_Job_NotExpiredOrFreeVault_IsUnchanged()
    {
        // Chuẩn bị: một kho còn hạn 30 ngày, một kho Free
        (HttpClient _, Guid paidVaultId) = await ApiTestHelpers.CreateClientWithPaidVaultAsync(factory);
        await SetServiceExpiresAtAsync(paidVaultId, DateTimeOffset.UtcNow.AddDays(30));
        HttpClient freeClient = await ApiTestHelpers.CreateClientForNewUserAsync(factory, emailVerified: true);
        Guid freeVaultId = await ApiTestHelpers.CreateVaultAsync(freeClient);

        // Hành động
        IReadOnlyList<Guid> found = await FindExpiredAsync();
        bool paidChanged = await ProcessAsync(paidVaultId);
        bool freeChanged = await ProcessAsync(freeVaultId);

        // Kiểm tra
        Assert.DoesNotContain(paidVaultId, found);
        Assert.DoesNotContain(freeVaultId, found);
        Assert.False(paidChanged);
        Assert.False(freeChanged);
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            Assert.Equal(EntitlementStatus.PaidActive,
                (await db.VaultEntitlements.SingleAsync(e => e.VaultId == paidVaultId)).Status);
            Assert.Equal(EntitlementStatus.Free,
                (await db.VaultEntitlements.SingleAsync(e => e.VaultId == freeVaultId)).Status);
        });
    }

    [SqlFact]
    public async Task Oplan01_DevTools_ServiceExpiredMilestone_MovesVaultToGrace()
    {
        // Chuẩn bị: kho XS còn hạn 1 năm, Admin mở công cụ tua thời gian
        (HttpClient _, Guid vaultId) = await ApiTestHelpers.CreateClientWithPaidVaultAsync(factory);
        await SetServiceExpiresAtAsync(vaultId, DateTimeOffset.UtcNow.AddDays(365));
        User admin = await DeathCaseTestData.AddUserAsync(factory, UserRole.Admin, UserStatus.Active);
        HttpClient adminClient = factory.CreateClient();
        ApiTestHelpers.AuthorizeAs(adminClient, factory.Services, admin);
        string clockUrl = $"/api/v1/admin/dev-tools/vaults/{vaultId}/clock";

        JsonElement before = await ApiTestHelpers.ReadJsonAsync(await adminClient.GetAsync(clockUrl));
        Assert.Equal("AVAILABLE", FindMilestone(before, "SERVICE_EXPIRED").GetProperty("state").GetString());

        // Hành động
        HttpResponseMessage travel = await adminClient.PostAsJsonAsync(
            $"/api/v1/admin/dev-tools/vaults/{vaultId}/time-travel", new { milestone = "SERVICE_EXPIRED" });

        // Kiểm tra: kho sang ân hạn ngay, mốc hiện là đã qua, tua lại bị từ chối
        Assert.Equal(HttpStatusCode.OK, travel.StatusCode);
        JsonElement after = await ApiTestHelpers.ReadJsonAsync(travel);
        Assert.Equal("PASSED", FindMilestone(after, "SERVICE_EXPIRED").GetProperty("state").GetString());
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            VaultEntitlement entitlement = await db.VaultEntitlements.SingleAsync(e => e.VaultId == vaultId);
            Assert.Equal(EntitlementStatus.GraceReadOnly, entitlement.Status);
            Assert.True(entitlement.ServiceExpiresAt <= DateTimeOffset.UtcNow);
        });
        HttpResponseMessage again = await adminClient.PostAsJsonAsync(
            $"/api/v1/admin/dev-tools/vaults/{vaultId}/time-travel", new { milestone = "SERVICE_EXPIRED" });
        await ApiTestHelpers.AssertProblemAsync(again, HttpStatusCode.Conflict, "DEV_TOOL_NOT_ALLOWED");
    }

    private static JsonElement FindMilestone(JsonElement clock, string key)
    {
        foreach (JsonElement milestone in clock.GetProperty("milestones").EnumerateArray())
        {
            if (milestone.GetProperty("key").GetString() == key)
            {
                return milestone;
            }
        }
        throw new Xunit.Sdk.XunitException("Không thấy mốc " + key);
    }

    private async Task SetServiceExpiresAtAsync(Guid vaultId, DateTimeOffset expiresAt)
    {
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            await db.VaultEntitlements.Where(e => e.VaultId == vaultId)
                .ExecuteUpdateAsync(s => s.SetProperty(e => e.ServiceExpiresAt, expiresAt));
        });
    }

    private async Task<IReadOnlyList<Guid>> FindExpiredAsync()
    {
        using (IServiceScope scope = factory.Services.CreateScope())
        {
            IEntitlementExpiryJob job = scope.ServiceProvider.GetRequiredService<IEntitlementExpiryJob>();
            return await job.FindExpiredVaultIdsAsync(CancellationToken.None);
        }
    }

    private async Task<bool> ProcessAsync(Guid vaultId)
    {
        using (IServiceScope scope = factory.Services.CreateScope())
        {
            IEntitlementExpiryJob job = scope.ServiceProvider.GetRequiredService<IEntitlementExpiryJob>();
            return await job.ProcessVaultAsync(vaultId, CancellationToken.None);
        }
    }
}
