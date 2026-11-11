using LegacyVault.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LegacyVault.Infrastructure.Data.Configurations;

public sealed class ExtraMailRuleConfiguration : IEntityTypeConfiguration<ExtraMailRule>
{
    public void Configure(EntityTypeBuilder<ExtraMailRule> builder)
    {
        builder.ToTable("ExtraMailRules");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Name).IsRequired().HasMaxLength(100);
        builder.Property(r => r.Subject).IsRequired().HasMaxLength(200);
        builder.Property(r => r.Title).IsRequired().HasMaxLength(200);
        builder.Property(r => r.Body).IsRequired().HasMaxLength(4000);
        builder.Property(r => r.ButtonText).IsRequired().HasMaxLength(80);
        builder.Property(r => r.Note).IsRequired().HasMaxLength(1000);
    }
}

public sealed class ExtraMailSendConfiguration : IEntityTypeConfiguration<ExtraMailSend>
{
    public void Configure(EntityTypeBuilder<ExtraMailSend> builder)
    {
        builder.ToTable("ExtraMailSends");
        builder.HasKey(s => s.Id);
        // Mỗi luật chỉ xử lý một lần cho mỗi kỳ: chặn gửi trùng ở tầng DB.
        builder.HasIndex(s => new { s.RuleId, s.PeriodId }).IsUnique();
    }
}
