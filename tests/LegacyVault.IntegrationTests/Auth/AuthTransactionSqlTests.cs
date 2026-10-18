using System.Data;
using LegacyVault.Application.Repositories;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;
using LegacyVault.Infrastructure.Data;
using LegacyVault.IntegrationTests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace LegacyVault.IntegrationTests.Auth;

/// <summary>Kiểm tra isolation/rollback thật chuyển từ AuthTransactionSqlChecks; chỉ dùng database test ngẫu nhiên.</summary>
public sealed class AuthTransactionSqlTests
{
    [SqlFact]
    public async Task SerializableRollsBackUserAndRefreshTokenWhenOperationFails()
    {
        await using var factory = ApiFactory.CreateForTempDatabase()!;
        try
        {
            await factory.MigrateAsync();
            using var scope = factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var unit = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var user = new User { Email = "rollback@example.test", FullName = "Rollback fixture", Role = UserRole.User, Status = UserStatus.Active, CreatedAt = DateTimeOffset.UtcNow };
            await Assert.ThrowsAsync<InvalidOperationException>(() => unit.ExecuteSerializableAsync<int>(async () =>
            {
                Assert.Equal(IsolationLevel.Serializable, db.Database.CurrentTransaction!.GetDbTransaction().IsolationLevel);
                db.Users.Add(user);
                db.RefreshTokens.Add(new RefreshToken { User = user, UserId = user.Id, TokenHash = new string('a', 64), FamilyId = Guid.NewGuid(), SessionStartedAt = DateTimeOffset.UtcNow, CreatedAt = DateTimeOffset.UtcNow, ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(30) });
                await unit.SaveChangesAsync(default);
                throw new InvalidOperationException("fixture operation failed");
            }, default));
            db.ChangeTracker.Clear();
            Assert.False(await db.Users.AnyAsync(value => value.Id == user.Id));
            Assert.False(await db.RefreshTokens.AnyAsync(value => value.UserId == user.Id));
        }
        finally
        {
            await factory.DropAsync();
        }
    }
}
