using LegacyVault.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LegacyVault.Infrastructure.Data.Configurations;

public sealed class HandoverGrantConfiguration : IEntityTypeConfiguration<HandoverGrant>
{
    public void Configure(EntityTypeBuilder<HandoverGrant> builder)
    {
        builder.ToTable("HandoverGrants");

        builder.HasKey(g => g.Id);

        builder.Property(g => g.Status)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(32);

        builder.Property(g => g.RowVersion)
            .IsRowVersion();

        // DATA-04: một lượt chỉ có một grant, kể cả khi hai yêu cầu nhận chạy song song.
        builder.HasIndex(g => g.TurnId)
            .IsUnique();

        // Job tìm grant quá hạn.
        builder.HasIndex(g => new { g.Status, g.ExpiresAt });

        builder.HasIndex(g => g.UserId);
    }
}
