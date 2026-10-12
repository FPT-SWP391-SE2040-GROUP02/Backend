using Microsoft.EntityFrameworkCore;

namespace LegacyVault.Infrastructure.Data;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    // Thêm DbSet<Entity> khi triển khai nghiệp vụ; bộ khung chưa có bảng.
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    }
}
