using LegacyVault.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LegacyVault.Infrastructure.Data.Configurations;

public sealed class LoginThrottleConfiguration : IEntityTypeConfiguration<LoginThrottle>
{
    public void Configure(EntityTypeBuilder<LoginThrottle> builder)
    {
        builder.ToTable("LoginThrottles");

        builder.HasKey(t => t.Id);

        builder.Property(t => t.Email)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(t => t.IpRange)
            .IsRequired()
            .HasMaxLength(64);

        // Mỗi cặp email và dải IP có đúng một bộ đếm lần đăng nhập sai.
        builder.HasIndex(t => new { t.Email, t.IpRange })
            .IsUnique();

        // Phục vụ tra theo dải IP và thời điểm bắt đầu đếm.
        builder.HasIndex(t => new { t.IpRange, t.WindowStartedAt });
    }
}
