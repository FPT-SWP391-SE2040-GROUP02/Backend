using LegacyVault.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LegacyVault.Infrastructure.Data.Configurations;

public sealed class EmailTemplateConfiguration : IEntityTypeConfiguration<EmailTemplate>
{
    public void Configure(EntityTypeBuilder<EmailTemplate> builder)
    {
        builder.ToTable("EmailTemplates");
        builder.HasKey(t => t.Key);
        builder.Property(t => t.Key).HasMaxLength(64);
        builder.Property(t => t.Subject).HasMaxLength(200);
        builder.Property(t => t.Title).HasMaxLength(200);
        builder.Property(t => t.Body).HasMaxLength(4000);
        builder.Property(t => t.ButtonText).HasMaxLength(80);
        builder.Property(t => t.Note).HasMaxLength(1000);
    }
}
