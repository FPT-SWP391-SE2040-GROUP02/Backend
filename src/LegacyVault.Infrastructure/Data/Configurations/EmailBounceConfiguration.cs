using LegacyVault.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LegacyVault.Infrastructure.Data.Configurations;

public sealed class EmailBounceConfiguration : IEntityTypeConfiguration<EmailBounce>
{
    public void Configure(EntityTypeBuilder<EmailBounce> builder)
    {
        builder.ToTable("EmailBounces");
        builder.HasKey(b => b.Id);
        builder.Property(b => b.EmailHash).IsRequired().HasMaxLength(64);
        builder.Property(b => b.FailureCode).IsRequired().HasMaxLength(16);
        builder.HasIndex(b => b.EmailHash).IsUnique();
    }
}
