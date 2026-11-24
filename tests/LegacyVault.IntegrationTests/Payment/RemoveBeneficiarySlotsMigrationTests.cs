using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;
using LegacyVault.Infrastructure.Data;
using LegacyVault.IntegrationTests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace LegacyVault.IntegrationTests.Payment;

/// <summary>
/// Migration RemoveBeneficiarySlots phải xóa các tham số Admin về suất người nhận (Plans.*.Slots, Plans.MaxTotalSlots),
/// giữ nguyên tham số khác của gói, và bỏ hai cột suất mà model hiện tại đọc lại được.
/// </summary>
public sealed class RemoveBeneficiarySlotsMigrationTests : IAsyncLifetime
{
    private const string PreviousMigration = "20261124102213_RemoveOwnerAliveLeftovers";

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
    public async Task Up_DeletesSlotSettings_KeepsOtherPlanSettings()
    {
        // Chuẩn bị: đưa schema về trước migration rồi chèn tham số kiểu cũ.
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            IMigrator migrator = db.GetService<IMigrator>();
            await migrator.MigrateAsync(PreviousMigration);

            await InsertSettingAsync(db, "Plans.MaxTotalSlots", "50");
            await InsertSettingAsync(db, "Plans.Free.Slots", "0");
            await InsertSettingAsync(db, "Plans.GOLD.Slots", "7");
            await InsertSettingAsync(db, "Plans.GOLD.QuotaMb", "1000");
            await InsertSettingAsync(db, "Plans.OrderTimeoutMinutes", "15");
        });

        // Hành động: áp migration mới nhất.
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            await db.Database.MigrateAsync();
        });

        // Kiểm tra: tham số suất bị xóa, tham số khác còn nguyên.
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            List<string> keys = await db.Set<SystemSetting>()
                .Where(s => s.Key.StartsWith("Plans."))
                .Select(s => s.Key)
                .ToListAsync();
            Assert.DoesNotContain("Plans.MaxTotalSlots", keys);
            Assert.DoesNotContain("Plans.Free.Slots", keys);
            Assert.DoesNotContain("Plans.GOLD.Slots", keys);
            Assert.Contains("Plans.GOLD.QuotaMb", keys);
            Assert.Contains("Plans.OrderTimeoutMinutes", keys);
        });
    }

    private static Task<int> InsertSettingAsync(AppDbContext db, string key, string value)
    {
        Guid id = Guid.NewGuid();
        return db.Database.ExecuteSqlAsync(
            $"INSERT INTO [SystemSettings] ([Id], [Key], [Value], [DataType], [Category], [Description], [IsEnabled], [UpdatedAt]) VALUES ({id}, {key}, {value}, 'NUMBER', 'Plans', N'Dữ liệu thử', 1, SYSDATETIMEOFFSET())");
    }
}
