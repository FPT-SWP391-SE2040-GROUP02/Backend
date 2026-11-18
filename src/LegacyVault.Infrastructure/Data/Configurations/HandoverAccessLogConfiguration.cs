using LegacyVault.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LegacyVault.Infrastructure.Data.Configurations;

/// <summary>Cấu hình bảng HandoverAccessLogs (nhật ký tải tài sản).</summary>
public sealed class HandoverAccessLogConfiguration : IEntityTypeConfiguration<HandoverAccessLog>
{
    public void Configure(EntityTypeBuilder<HandoverAccessLog> builder)
    {
        builder.ToTable("HandoverAccessLogs");

        builder.HasKey(l => l.Id);

        builder.Property(l => l.Outcome)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(32);

        builder.HasIndex(l => new { l.GrantId, l.At });
    }
}
