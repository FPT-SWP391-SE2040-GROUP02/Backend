using LegacyVault.Application.Repositories;
using LegacyVault.Application.Services;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;
using LegacyVault.IntegrationTests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace LegacyVault.IntegrationTests.Audit;

/// <summary>
/// Bảng AuditLogs trên SQL Server thật (database tạm). [SqlFact] chỉ chạy khi có LEGACYVAULT_TEST_CONNECTION.
/// </summary>
public sealed class AuditLogRepositoryTests : IAsyncLifetime
{
    private static readonly DateTimeOffset Now = new DateTimeOffset(2026, 10, 15, 8, 0, 0, TimeSpan.Zero);

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
    public async Task Sec07_AddThenSave_StoresEnumsAsText()
    {
        // Chuẩn bị: một Owner có kho
        HttpClient client = await ApiTestHelpers.CreateClientForNewUserAsync(factory, emailVerified: true);
        Guid vaultId = await ApiTestHelpers.CreateVaultAsync(client);
        Guid ownerId = Guid.Empty;
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            Vault vault = await db.Vaults.SingleAsync(v => v.Id == vaultId);
            ownerId = vault.OwnerId;
        });

        // Hành động: ghi một dòng qua repository rồi lưu bằng IUnitOfWork, đúng cách service sẽ dùng
        AuditLog log = AuditRules.OwnerAction(
            vaultId, ownerId, AuditAction.VaultUpdated, AuditTargetType.Vault, vaultId, null, Now);
        using (IServiceScope scope = factory.Services.CreateScope())
        {
            IAuditLogRepository repository = scope.ServiceProvider.GetRequiredService<IAuditLogRepository>();
            IUnitOfWork unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            repository.Add(log);
            await unitOfWork.SaveChangesAsync(CancellationToken.None);
        }

        // Kiểm tra: dòng đã lưu, enum lưu dạng chữ để đọc được khi tra cứu trực tiếp
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            AuditLog stored = await db.AuditLogs.AsNoTracking().SingleAsync(l => l.Id == log.Id);
            Assert.Equal(ownerId, stored.ActorUserId);
            Assert.Equal(AuditAction.VaultUpdated, stored.Action);

            string action = await db.Database
                .SqlQuery<string>($"SELECT [Action] AS [Value] FROM [AuditLogs] WHERE [Id] = {log.Id}")
                .SingleAsync();
            Assert.Equal("VaultUpdated", action);
        });
    }

    [SqlFact]
    public async Task Data07_UnknownVaultIsRejectedByForeignKey()
    {
        // Hành động: dòng nhật ký trỏ tới kho không tồn tại
        AuditLog log = AuditRules.Create(
            Guid.NewGuid(), null, AuditActorRole.System, AuditAction.PlanDeactivated, AuditTargetType.Plan,
            null, null, AuditOutcome.Succeeded, null, Now);

        // Kiểm tra: khóa ngoại chặn, không có dòng mồ côi
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            db.AuditLogs.Add(log);
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        });
    }
}
