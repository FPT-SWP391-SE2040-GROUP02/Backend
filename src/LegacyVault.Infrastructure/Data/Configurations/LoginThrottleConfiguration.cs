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
        builder.Property(t => t.Email).HasMaxLength(320).IsRequired();
        builder.Property(t => t.IpRange).HasMaxLength(64).IsRequired();
        builder.HasIndex(t => new { t.Email, t.IpRange }).IsUnique();
    }
}
