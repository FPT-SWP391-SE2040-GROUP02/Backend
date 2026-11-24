using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;
using LegacyVault.Infrastructure.Data;
using LegacyVault.IntegrationTests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace LegacyVault.IntegrationTests.Handover;

/// <summary>
/// Migration RemoveOwnerAliveLeftovers phải dọn hàng cũ của luồng "Chủ kho báo còn sống": kế hoạch Blocked trở về Active,
/// dòng nhật ký ObjectionReported/ObjectionConcluded bị xóa, dòng nhật ký khác giữ nguyên; đọc lại được bằng model hiện tại.
/// </summary>
public sealed class RemoveOwnerAliveLeftoversMigrationTests : IAsyncLifetime
{
    private const string PreviousMigration = "20261124082950_RemoveExtraSlotPurchase";

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
    public async Task Up_ResetsBlockedPlansAndDeletesObjectionAuditRows_KeepsOtherRows()
    {
        // Chuẩn bị: kho thật, rồi đưa schema về trước migration để chèn dữ liệu kiểu cũ.
        User owner = await VaultTestData.AddUserAsync(factory, UserRole.User, UserStatus.Active);
        HttpClient client = factory.CreateClient();
        ApiTestHelpers.AuthorizeAs(client, factory.Services, owner);
        Guid vaultId = await ApiTestHelpers.CreateVaultAsync(client);
        Guid keptId = Guid.NewGuid();

        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            IMigrator migrator = db.GetService<IMigrator>();
            await migrator.MigrateAsync(PreviousMigration);

            await db.Database.ExecuteSqlAsync($"UPDATE [HandoverPlans] SET [Status] = 'Blocked' WHERE [VaultId] = {vaultId}");
            await InsertAuditAsync(db, Guid.NewGuid(), vaultId, "ObjectionReported");
            await InsertAuditAsync(db, Guid.NewGuid(), vaultId, "ObjectionConcluded");
            await InsertAuditAsync(db, keptId, vaultId, "DeathCaseOpened");
        });

        // Hành động: áp migration mới nhất.
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            await db.Database.MigrateAsync();
        });

        // Kiểm tra: đọc lại bằng model hiện tại không lỗi, chỉ còn dòng hợp lệ.
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            Assert.Equal(PlanStatus.Active, (await db.HandoverPlans.SingleAsync(p => p.VaultId == vaultId)).Status);
            List<AuditLog> logs = await db.Set<AuditLog>().Where(l => l.VaultId == vaultId).ToListAsync();
            AuditLog kept = Assert.Single(logs, l => l.Reason == "Dữ liệu thử");
            Assert.Equal(keptId, kept.Id);
            Assert.Equal(AuditAction.DeathCaseOpened, kept.Action);
        });
    }

    private static Task<int> InsertAuditAsync(AppDbContext db, Guid id, Guid vaultId, string action)
    {
        return db.Database.ExecuteSqlAsync(
            $"INSERT INTO [AuditLogs] ([Id], [VaultId], [ActorRole], [Action], [TargetType], [Outcome], [Reason], [At]) VALUES ({id}, {vaultId}, 'System', {action}, 'DeathCase', 'Succeeded', N'Dữ liệu thử', SYSDATETIMEOFFSET())");
    }
}
