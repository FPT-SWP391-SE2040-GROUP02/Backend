using LegacyVault.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LegacyVault.Infrastructure.Data.Configurations;

public sealed class ExternalLoginConfiguration : IEntityTypeConfiguration<ExternalLogin>
{
    public void Configure(EntityTypeBuilder<ExternalLogin> builder)
    {
        builder.ToTable("ExternalLogins");
        builder.HasKey(l => l.Id);
        builder.Property(l => l.Provider).HasConversion<string>().HasMaxLength(32);
        builder.Property(l => l.ProviderSubject).HasMaxLength(255).IsRequired();
        builder.Property(l => l.ProviderEmail).HasMaxLength(320);
        builder.HasIndex(l => new { l.Provider, l.ProviderSubject }).IsUnique();
        builder.HasIndex(l => new { l.UserId, l.Provider }).IsUnique();
        builder.HasOne(l => l.User).WithMany().HasForeignKey(l => l.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}
