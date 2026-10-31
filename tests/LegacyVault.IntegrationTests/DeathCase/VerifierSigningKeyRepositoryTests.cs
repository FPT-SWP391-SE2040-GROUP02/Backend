using LegacyVault.Application.Exceptions;
using LegacyVault.Application.Repositories;
using LegacyVault.Application.Services;
using LegacyVault.Domain.Entities;
using LegacyVault.IntegrationTests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace LegacyVault.IntegrationTests.DeathCases;

/// <summary>
/// DEATH-14: ràng buộc DB của khóa ký Verifier và bộ đếm nhập sai mật khẩu ký trên SQL Server thật (database tạm).
/// </summary>
public sealed class VerifierSigningKeyRepositoryTests : IAsyncLifetime
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
    public async Task Death14_DatabaseRejectsSecondActiveKeyForVerifier()
    {
        // Chuẩn bị: Verifier đã có một khóa đang dùng
        User verifier = await DeathCaseTestData.AddVerifierAsync(factory);
        DbUpdateException? firstError = await TryAddKeyAsync(CreateKey(verifier.Id, null));

        // Hành động
        DbUpdateException? secondError = await TryAddKeyAsync(CreateKey(verifier.Id, null));

        // Kiểm tra: DB chặn bằng chỉ mục unique có lọc
        Assert.Null(firstError);
        Assert.NotNull(secondError);
        using (IServiceScope scope = factory.Services.CreateScope())
        {
            IDeathCaseRepository repository = scope.ServiceProvider.GetRequiredService<IDeathCaseRepository>();
            Assert.True(repository.IsUniqueViolation(secondError));
        }
    }

    [SqlFact]
    public async Task Death14_RevokedKeyDoesNotBlockNewKey()
    {
        // Chuẩn bị: Verifier có một khóa đã thu hồi
        User verifier = await DeathCaseTestData.AddVerifierAsync(factory);
        VerifierSigningKey revoked = CreateKey(verifier.Id, DateTimeOffset.UtcNow);
        VerifierSigningKey active = CreateKey(verifier.Id, null);
        await TryAddKeyAsync(revoked);

        // Hành động
        DbUpdateException? error = await TryAddKeyAsync(active);
        VerifierSigningKey? found;
        VerifierSigningKey? revokedById;
        using (IServiceScope scope = factory.Services.CreateScope())
        {
            IVerifierSigningKeyRepository repository = scope.ServiceProvider.GetRequiredService<IVerifierSigningKeyRepository>();
            found = await repository.FindActiveByUserIdAsync(verifier.Id, CancellationToken.None);
            revokedById = await repository.FindByIdAsync(revoked.Id, CancellationToken.None);
        }

        // Kiểm tra: khóa mới lưu được, tra khóa đang dùng ra khóa mới, khóa cũ vẫn đọc được theo Id
        Assert.Null(error);
        Assert.NotNull(found);
        Assert.Equal(active.Id, found.Id);
        Assert.Equal(active.PublicKey, found.PublicKey);
        Assert.NotNull(revokedById);
        Assert.NotNull(revokedById.RevokedAt);
    }

    [SqlFact]
    public async Task Death14_FindActiveReturnsNullWhenNoKey()
    {
        // Chuẩn bị
        User verifier = await DeathCaseTestData.AddVerifierAsync(factory);

        // Hành động
        VerifierSigningKey? found;
        VerifierSigningGuard? guard;
        using (IServiceScope scope = factory.Services.CreateScope())
        {
            IVerifierSigningKeyRepository repository = scope.ServiceProvider.GetRequiredService<IVerifierSigningKeyRepository>();
            found = await repository.FindActiveByUserIdAsync(verifier.Id, CancellationToken.None);
            guard = await repository.FindGuardAsync(verifier.Id, CancellationToken.None);
        }

        // Kiểm tra
        Assert.Null(found);
        Assert.Null(guard);
    }

    [SqlFact]
    public async Task Death14_GuardRowVersionDetectsConcurrentUpdate()
    {
        // Chuẩn bị: Verifier đã nhập sai một lần
        User verifier = await DeathCaseTestData.AddVerifierAsync(factory);
        using (IServiceScope scope = factory.Services.CreateScope())
        {
            IVerifierSigningKeyRepository repository = scope.ServiceProvider.GetRequiredService<IVerifierSigningKeyRepository>();
            IUnitOfWork unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            VerifierSigningGuard guard = new VerifierSigningGuard();
            guard.UserId = verifier.Id;
            guard.FailedCount = 1;
            guard.UpdatedAt = DateTimeOffset.UtcNow;
            repository.AddGuard(guard);
            await unitOfWork.SaveChangesAsync(CancellationToken.None);
        }

        // Hành động: hai request cùng đọc bộ đếm rồi cùng tăng
        Exception? secondError;
        using (IServiceScope first = factory.Services.CreateScope())
        using (IServiceScope second = factory.Services.CreateScope())
        {
            IVerifierSigningKeyRepository firstRepository = first.ServiceProvider.GetRequiredService<IVerifierSigningKeyRepository>();
            IVerifierSigningKeyRepository secondRepository = second.ServiceProvider.GetRequiredService<IVerifierSigningKeyRepository>();
            VerifierSigningGuard? firstGuard = await firstRepository.FindGuardAsync(verifier.Id, CancellationToken.None);
            VerifierSigningGuard? secondGuard = await secondRepository.FindGuardAsync(verifier.Id, CancellationToken.None);
            Assert.NotNull(firstGuard);
            Assert.NotNull(secondGuard);

            firstGuard.FailedCount = firstGuard.FailedCount + 1;
            await first.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(CancellationToken.None);

            secondGuard.FailedCount = secondGuard.FailedCount + 1;
            secondError = await Record.ExceptionAsync(() =>
                second.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(CancellationToken.None));
        }

        // Kiểm tra: bên lưu sau bị phát hiện tranh chấp (IUnitOfWork đổi thành 409 EDIT_CONFLICT), DB giữ đúng 2 lần sai
        AppException conflict = Assert.IsType<AppException>(secondError);
        Assert.Equal(AppErrorType.Conflict, conflict.Type);
        Assert.Equal(ErrorCode.EditConflict, conflict.Code);
        int savedCount = 0;
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            VerifierSigningGuard saved = await db.Set<VerifierSigningGuard>().AsNoTracking().SingleAsync(g => g.UserId == verifier.Id);
            savedCount = saved.FailedCount;
        });
        Assert.Equal(2, savedCount);
    }

    private async Task<DbUpdateException?> TryAddKeyAsync(VerifierSigningKey key)
    {
        using (IServiceScope scope = factory.Services.CreateScope())
        {
            IVerifierSigningKeyRepository repository = scope.ServiceProvider.GetRequiredService<IVerifierSigningKeyRepository>();
            IUnitOfWork unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            repository.Add(key);
            try
            {
                await unitOfWork.SaveChangesAsync(CancellationToken.None);
                return null;
            }
            catch (DbUpdateException ex)
            {
                return ex;
            }
        }
    }

    /// <summary>Khóa giả chỉ để kiểm ràng buộc DB; các mảng byte không phải khóa thật.</summary>
    private static VerifierSigningKey CreateKey(Guid userId, DateTimeOffset? revokedAt)
    {
        VerifierSigningKey key = new VerifierSigningKey();
        key.UserId = userId;
        key.Algorithm = DecisionSignatureRules.Algorithm;
        key.PublicKey = Guid.NewGuid().ToByteArray();
        key.EncryptionKeyId = "test-key";
        key.WrappedDataKey = new byte[60];
        key.PrivateKeyEncrypted = new byte[64];
        key.CreatedAt = DateTimeOffset.UtcNow;
        key.RevokedAt = revokedAt;
        return key;
    }
}
