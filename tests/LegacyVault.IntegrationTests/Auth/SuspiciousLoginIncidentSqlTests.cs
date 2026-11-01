using LegacyVault.Application.Dtos.Requests;
using LegacyVault.Application.Security;
using LegacyVault.Application.Services;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;
using LegacyVault.IntegrationTests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace LegacyVault.IntegrationTests.Auth;

/// <summary>
/// Cảnh báo đăng nhập bất thường (SEC-10) trên SQL Server database tạm: sự cố được lưu cùng lần khóa
/// và giao cho Admin; tài khoản đang có sự cố mở thì bị khóa thêm lần nữa cũng không mở sự cố thứ hai.
/// </summary>
public sealed class SuspiciousLoginIncidentSqlTests : IAsyncLifetime
{
    private const string Password = "Correct-Password-1";

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

    private async Task<User> AddUserAsync(UserRole role)
    {
        IPasswordHasher hasher = factory.Services.GetRequiredService<IPasswordHasher>();
        User user = new User();
        user.Email = $"{Guid.NewGuid():N}@test.local";
        user.FullName = "Người thử";
        user.Role = role;
        user.Status = UserStatus.Active;
        user.EmailVerifiedAt = DateTimeOffset.UtcNow;
        user.CreatedAt = DateTimeOffset.UtcNow;
        user.PasswordHash = hasher.Hash(Password);
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            db.Users.Add(user);
            await db.SaveChangesAsync();
        });
        return user;
    }

    private async Task LoginWrongFiveTimesAsync(string email, string ipRange)
    {
        for (int attempt = 1; attempt <= 5; attempt++)
        {
            using (IServiceScope scope = factory.Services.CreateScope())
            {
                IAuthService auth = scope.ServiceProvider.GetRequiredService<IAuthService>();
                LoginRequest request = new LoginRequest();
                request.Email = email;
                request.Password = "wrong-password";
                await Assert.ThrowsAnyAsync<Exception>(() => auth.LoginAsync(request, ipRange, default));
            }
        }
    }

    private async Task<List<Incident>> LoadSuspiciousLoginIncidentsAsync()
    {
        List<Incident> incidents = new List<Incident>();
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            incidents = await db.Set<Incident>().AsNoTracking()
                .Where(i => i.Type == IncidentType.SuspiciousLogin).ToListAsync();
        });
        return incidents;
    }

    [SqlFact]
    public async Task Sec10_FifthWrongPassword_OpensSuspiciousLoginIncidentAssignedToAdmin()
    {
        User admin = await AddUserAsync(UserRole.Admin);
        User target = await AddUserAsync(UserRole.User);

        await LoginWrongFiveTimesAsync(target.Email, "198.51.100.0/24");

        Incident incident = Assert.Single(await LoadSuspiciousLoginIncidentsAsync());
        Assert.Equal(IncidentStatus.Open, incident.Status);
        Assert.Equal(IncidentRules.TargetUser, incident.TargetType);
        Assert.Equal(target.Id, incident.TargetId);
        Assert.Null(incident.VaultId);
        Assert.Equal(admin.Id, incident.AssigneeUserId);
    }

    [SqlFact]
    public async Task Sec10_LockedAgainFromOtherNetworkWhileIncidentOpen_KeepsOneIncident()
    {
        await AddUserAsync(UserRole.Admin);
        User target = await AddUserAsync(UserRole.User);

        await LoginWrongFiveTimesAsync(target.Email, "198.51.100.0/24");
        await LoginWrongFiveTimesAsync(target.Email, "203.0.113.0/24");

        Assert.Single(await LoadSuspiciousLoginIncidentsAsync());
    }
}
