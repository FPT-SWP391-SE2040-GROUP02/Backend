using LegacyVault.Application.Repositories;
using LegacyVault.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace LegacyVault.Infrastructure.Data;

/// <summary>
/// Kết nối EF Core tới SQL Server. Mỗi HTTP request dùng một instance (Scoped), mọi repository trong request
/// dùng chung instance này nên một lần SaveChanges lưu được thay đổi của nhiều bảng (STATE-02).
/// </summary>
/// <remarks>
/// Cấu hình bảng (khóa, index unique, độ dài cột, enum lưu dạng chữ...) đặt ở Data/Configurations,
/// mỗi entity một class IEntityTypeConfiguration; ApplyConfigurationsFromAssembly tự nạp hết.
/// Chưa có configuration thì EF dùng quy ước mặc định — phải viết configuration trước khi tạo migration.
/// </remarks>
public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options), IUnitOfWork
{
    // Tài khoản và đăng nhập
    public DbSet<User> Users => Set<User>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<EmailVerificationToken> EmailVerificationTokens => Set<EmailVerificationToken>();
    public DbSet<LoginThrottle> LoginThrottles => Set<LoginThrottle>();
    public DbSet<ExternalLogin> ExternalLogins => Set<ExternalLogin>();
    public DbSet<ExternalLoginLinkTicket> ExternalLoginLinkTickets => Set<ExternalLoginLinkTicket>();

    // Kho
    public DbSet<Vault> Vaults => Set<Vault>();
    public DbSet<HandoverPlan> HandoverPlans => Set<HandoverPlan>();
    public DbSet<VaultEntitlement> VaultEntitlements => Set<VaultEntitlement>();
    public DbSet<Container> Containers => Set<Container>();
    public DbSet<ContainerAsset> ContainerAssets => Set<ContainerAsset>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    }

    // DbContext.SaveChangesAsync trả Task<int>, còn IUnitOfWork trả Task → cài đặt tường minh.
    Task IUnitOfWork.SaveChangesAsync(CancellationToken ct)
    {
        return SaveChangesAsync(ct);
    }
}
