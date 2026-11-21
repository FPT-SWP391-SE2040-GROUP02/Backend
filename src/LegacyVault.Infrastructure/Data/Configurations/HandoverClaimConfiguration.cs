using LegacyVault.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LegacyVault.Infrastructure.Data.Configurations;

public sealed class HandoverClaimConfiguration : IEntityTypeConfiguration<HandoverClaim>
{
    public void Configure(EntityTypeBuilder<HandoverClaim> builder)
    {
        builder.ToTable("HandoverClaims");
        builder.HasKey(c => c.Id);

        builder.Property(c => c.Status)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(32);
        builder.Property(c => c.Note).IsRequired().HasMaxLength(1000);
        builder.Property(c => c.ReviewReason).HasMaxLength(1000);
        builder.Property(c => c.UpdatedAt).IsConcurrencyToken();

        // Mỗi người nhận chỉ có một yêu cầu trong một biên bản: gửi lại hai lần cùng lúc thì lần sau vi phạm unique (409).
        builder.HasIndex(c => new { c.RecordId, c.BeneficiaryId })
            .IsUnique();
        builder.HasIndex(c => c.UserId);
    }
}
