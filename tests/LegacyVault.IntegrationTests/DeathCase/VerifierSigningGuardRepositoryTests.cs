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
/// DEATH-03: ràng buộc DB của bộ đếm nhập sai mật khẩu xác nhận duyệt/từ chối trên SQL Server thật (database tạm).
/// </summary>
public sealed class VerifierSigningGuardRepositoryTests : IAsyncLifetime
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
    public async Task Death03_GuardRowVersionDetectsConcurrentUpdate()
    {
        // Chuẩn bị: Verifier đã nhập sai một lần
        User verifier = await DeathCaseTestData.AddVerifierAsync(factory);
        using (IServiceScope scope = factory.Services.CreateScope())
        {
            IVerifierSigningGuardRepository repository = scope.ServiceProvider.GetRequiredService<IVerifierSigningGuardRepository>();
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
            IVerifierSigningGuardRepository firstRepository = first.ServiceProvider.GetRequiredService<IVerifierSigningGuardRepository>();
            IVerifierSigningGuardRepository secondRepository = second.ServiceProvider.GetRequiredService<IVerifierSigningGuardRepository>();
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
}
