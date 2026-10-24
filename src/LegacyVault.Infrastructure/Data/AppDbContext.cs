using LegacyVault.Application.Exceptions;
using LegacyVault.Application.Repositories;
using LegacyVault.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.SqlClient;

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

    // Tài sản
    public DbSet<Asset> Assets => Set<Asset>();

    // Người nhận
    public DbSet<Beneficiary> Beneficiaries => Set<Beneficiary>();
    public DbSet<Designation> Designations => Set<Designation>();

    // Điểm danh (deadman switch)
    public DbSet<CheckInSchedule> CheckInSchedules => Set<CheckInSchedule>();
    public DbSet<CheckInPeriod> CheckInPeriods => Set<CheckInPeriod>();
    public DbSet<CheckInReminder> CheckInReminders => Set<CheckInReminder>();

    /// <summary>Transaction Auth giữ khóa từ lúc đọc refresh token tới commit; dispose rollback khi operation lỗi.</summary>
    public async Task<T> ExecuteSerializableAsync<T>(Func<Task<T>> operation, CancellationToken ct)
    {
        await using var transaction = await Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
        try
        {
            var result = await operation();
            await transaction.CommitAsync(ct);
            return result;
        }
        catch (Exception exception) when (exception.GetBaseException() is SqlException { Number: 1205 })
        {
            // Serializable read-modify-write may select a deadlock victim. Dispose rolls back; never retry a mutation.
            throw new AppException(AppErrorType.Conflict, ErrorCode.EditConflict,
                "Dữ liệu đang được cập nhật đồng thời. Tải lại trước khi thử lại.");
        }
    }
    // Xác minh danh tính (eKYC)
    public DbSet<EkycVerification> EkycVerifications => Set<EkycVerification>();
    public DbSet<EkycConsent> EkycConsents => Set<EkycConsent>();
    public DbSet<OwnerFaceTemplate> OwnerFaceTemplates => Set<OwnerFaceTemplate>();
    public DbSet<OwnerIdentity> OwnerIdentities => Set<OwnerIdentity>();

    // Nhật ký hoạt động (SEC-07, SEC-13)
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    // Thanh toán (SRS §8)
    public DbSet<PaymentOrder> PaymentOrders => Set<PaymentOrder>();
    public DbSet<PaymentTransaction> PaymentTransactions => Set<PaymentTransaction>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    }

    // DbContext.SaveChangesAsync trả Task<int>, còn IUnitOfWork trả Task → cài đặt tường minh.
    // Concurrency token (UpdatedAt) lệch nghĩa là request khác vừa sửa cùng bản ghi → 409 EDIT_CONFLICT,
    // để Application không phải biết exception của EF.
    async Task IUnitOfWork.SaveChangesAsync(CancellationToken ct)
    {
        try
        {
            await SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new AppException(AppErrorType.Conflict, ErrorCode.EditConflict,
                "Dữ liệu vừa được sửa ở nơi khác. Vui lòng tải lại rồi sửa tiếp.");
        }
    }
}
