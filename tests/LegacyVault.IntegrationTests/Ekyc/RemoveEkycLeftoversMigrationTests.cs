using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;
using LegacyVault.Infrastructure.Data;
using LegacyVault.IntegrationTests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace LegacyVault.IntegrationTests.Ekyc;

/// <summary>
/// Migration RemoveEkycLeftovers phải xử lý hàng cũ: lượt/đồng ý eKYC không phải OwnerEnrollment và hold OwnerAlive bị xóa,
/// bảng OwnerFaceTemplates bị bỏ, còn lượt OwnerEnrollment và hold khác giữ nguyên (đọc lại được bằng model hiện tại).
/// </summary>
public sealed class RemoveEkycLeftoversMigrationTests : IAsyncLifetime
{
    private const string PreviousMigration = "20261117064204_AddWillVersions";

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
    public async Task Up_RemovesLegacyPurposesOwnerAliveHoldsAndFaceTemplates_KeepsOwnerEnrollment()
    {
        // Chuẩn bị: kho và tài khoản thật, rồi đưa schema về trước migration để chèn dữ liệu kiểu cũ.
        User owner = await VaultTestData.AddUserAsync(factory, UserRole.User, UserStatus.Active);
        HttpClient client = factory.CreateClient();
        ApiTestHelpers.AuthorizeAs(client, factory.Services, owner);
        Guid vaultId = await ApiTestHelpers.CreateVaultAsync(client);
        Guid enrollmentId = Guid.NewGuid();

        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            IMigrator migrator = db.GetService<IMigrator>();
            await migrator.MigrateAsync(PreviousMigration);

            await InsertVerificationAsync(db, enrollmentId, owner.Id, "OwnerEnrollment", "NotChecked", null);
            await InsertVerificationAsync(db, Guid.NewGuid(), owner.Id, "BeneficiaryClaim", "Matched", Guid.NewGuid());
            await InsertVerificationAsync(db, Guid.NewGuid(), owner.Id, "OwnerAlive", "NotChecked", null);
            await InsertConsentAsync(db, owner.Id, "OwnerEnrollment");
            await InsertConsentAsync(db, owner.Id, "BeneficiaryClaim");
            await InsertConsentAsync(db, owner.Id, "OwnerAlive");
            await InsertHoldAsync(db, vaultId, "OwnerAlive");
            await InsertHoldAsync(db, vaultId, "Dispute");
            Guid templateId = Guid.NewGuid();
            byte[] embedding = new byte[] { 2 };
            byte[] wrappedKey = new byte[60];
            await db.Database.ExecuteSqlAsync(
                $"INSERT INTO [OwnerFaceTemplates] ([Id], [UserId], [CreatedAt], [Dimension], [EmbeddingEncrypted], [EncryptionKeyId], [ModelName], [SourceVerificationId], [Version], [WrappedDataKey]) VALUES ({templateId}, {owner.Id}, SYSDATETIMEOFFSET(), 128, {embedding}, 'test', 'sface', {enrollmentId}, 1, {wrappedKey})");
        });

        // Hành động: áp migration mới nhất.
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            await db.Database.MigrateAsync();
        });

        // Kiểm tra: đọc lại bằng model hiện tại không lỗi (enum chỉ còn giá trị hợp lệ), chỉ còn dữ liệu đúng.
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            List<EkycVerification> verifications = await db.EkycVerifications.ToListAsync();
            EkycVerification kept = Assert.Single(verifications);
            Assert.Equal(enrollmentId, kept.Id);
            Assert.Equal(EkycPurpose.OwnerEnrollment, kept.Purpose);

            List<EkycConsent> consents = await db.EkycConsents.ToListAsync();
            Assert.Equal(EkycPurpose.OwnerEnrollment, Assert.Single(consents).Purpose);

            List<Hold> holds = await db.Set<Hold>().ToListAsync();
            Assert.Equal(HoldType.Dispute, Assert.Single(holds).Type);

            int faceTemplateTables = await db.Database
                .SqlQueryRaw<int>("SELECT COUNT(*) AS [Value] FROM sys.tables WHERE name = 'OwnerFaceTemplates'")
                .SingleAsync();
            Assert.Equal(0, faceTemplateTables);

            int removedColumns = await db.Database
                .SqlQueryRaw<int>("SELECT COUNT(*) AS [Value] FROM sys.columns WHERE object_id = OBJECT_ID('EkycVerifications') "
                    + "AND name IN ('HandoverTurnId', 'IdentityMatch')")
                .SingleAsync();
            Assert.Equal(0, removedColumns);
        });
    }

    private static Task<int> InsertVerificationAsync(
        AppDbContext db, Guid id, Guid userId, string purpose, string identityMatch, Guid? handoverTurnId)
    {
        return db.Database.ExecuteSqlAsync(
            $"INSERT INTO [EkycVerifications] ([Id], [UserId], [Status], [Purpose], [IdentityMatch], [HandoverTurnId], [BackSideChecked], [LivenessPassed], [CitizenIdLast4], [CreatedAt]) VALUES ({id}, {userId}, 'Passed', {purpose}, {identityMatch}, {handoverTurnId}, 0, 1, '1234', SYSDATETIMEOFFSET())");
    }

    private static Task<int> InsertConsentAsync(AppDbContext db, Guid userId, string purpose)
    {
        Guid id = Guid.NewGuid();
        return db.Database.ExecuteSqlAsync(
            $"INSERT INTO [EkycConsents] ([Id], [UserId], [Purpose], [NoticeVersion], [ConsentedAt]) VALUES ({id}, {userId}, {purpose}, '2026-10-v1', SYSDATETIMEOFFSET())");
    }

    private static Task<int> InsertHoldAsync(AppDbContext db, Guid vaultId, string type)
    {
        Guid id = Guid.NewGuid();
        return db.Database.ExecuteSqlAsync(
            $"INSERT INTO [Holds] ([Id], [VaultId], [Scope], [Type], [Status], [Reason], [ReviewAt], [CreatedAt]) VALUES ({id}, {vaultId}, 'Vault', {type}, 'Active', N'Dữ liệu thử', SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET())");
    }
}
