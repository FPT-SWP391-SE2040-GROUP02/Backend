using LegacyVault.Application.Mappings;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;
using LegacyVault.IntegrationTests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace LegacyVault.IntegrationTests.ServiceConfig;

/// <summary>Bảng SystemConfigs: mỗi cặp (dịch vụ, ô) chỉ có một dòng; hai hành động nhật ký mới có tên dây đúng.</summary>
public sealed class SystemConfigSchemaTests : IAsyncLifetime
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

    private static SystemConfig NewRow(string serviceKey, SystemConfigSlot slot)
    {
        SystemConfig row = new SystemConfig();
        row.ServiceKey = serviceKey;
        row.Slot = slot;
        row.Enabled = true;
        row.SettingsJson = "{}";
        row.Version = 1;
        row.UpdatedAt = DateTimeOffset.UtcNow;
        row.UpdatedByUserId = Guid.NewGuid();
        row.UpdateReason = "test";
        return row;
    }

    [SqlFact]
    public async Task DuplicateServiceAndSlot_IsRejected()
    {
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            db.SystemConfigs.Add(NewRow("GEMINI", SystemConfigSlot.Primary));
            await db.SaveChangesAsync();
        });

        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            db.SystemConfigs.Add(NewRow("GEMINI", SystemConfigSlot.Primary));
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        });
    }

    [SqlFact]
    public async Task SameServiceInTwoSlots_IsAllowed()
    {
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            db.SystemConfigs.Add(NewRow("GEMINI", SystemConfigSlot.Primary));
            db.SystemConfigs.Add(NewRow("GEMINI", SystemConfigSlot.Fallback));
            await db.SaveChangesAsync();
            Assert.Equal(2, await db.SystemConfigs.CountAsync());
        });
    }

    [Fact]
    public void AuditActions_UseUpperSnakeWireValue()
    {
        Assert.Equal("SYSTEM_CONFIG_UPDATED", AuditAction.SystemConfigUpdated.ToApiString());
        Assert.Equal("SYSTEM_CONFIG_RESET", AuditAction.SystemConfigReset.ToApiString());
    }
}
