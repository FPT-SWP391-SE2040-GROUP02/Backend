using LegacyVault.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LegacyVault.Infrastructure.Data.Configurations;

public sealed class HandoverTemplateSetConfiguration : IEntityTypeConfiguration<HandoverTemplateSet>
{
    public void Configure(EntityTypeBuilder<HandoverTemplateSet> builder)
    {
        builder.ToTable("HandoverTemplateSets");
        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id).ValueGeneratedOnAdd();
        builder.Property(t => t.AcceptJson).IsRequired();
        builder.Property(t => t.DeclineJson).IsRequired();
        builder.Property(t => t.DeclineNotice).IsRequired().HasMaxLength(500);
        builder.Property(t => t.Note).HasMaxLength(300);
    }
}
