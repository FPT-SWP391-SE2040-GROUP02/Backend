using LegacyVault.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LegacyVault.Infrastructure.Data.Configurations;

public sealed class HandoverPlanConfiguration : IEntityTypeConfiguration<HandoverPlan>
{
    public void Configure(EntityTypeBuilder<HandoverPlan> builder)
    {
        builder.ToTable("HandoverPlans");

        builder.HasKey(p => p.Id);

        builder.Property(p => p.Status)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(32);

        builder.HasIndex(p => p.VaultId)
            .IsUnique();
    }
}
