using LegacyVault.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LegacyVault.Infrastructure.Data.Configurations;

public sealed class DeathCaseNoticeConfiguration : IEntityTypeConfiguration<DeathCaseNotice>
{
    public void Configure(EntityTypeBuilder<DeathCaseNotice> builder)
    {
        builder.ToTable("DeathCaseNotices");

        builder.HasKey(n => n.Id);

        builder.Property(n => n.Channel)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(32);

        builder.Property(n => n.Status)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(32);

        // D3, TIME-02: hai job cùng xử lý một thông báo thì bên lưu sau bị xung đột, không gửi trùng trạng thái.
        builder.Property(n => n.UpdatedAt)
            .IsConcurrencyToken();

        // Job quét thông báo Pending cũ nhất trước.
        builder.HasIndex(n => new { n.Status, n.CreatedAt });

        // Không lưu địa chỉ email: job đọc email Owner qua RecipientUserId lúc gửi.
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(n => n.RecipientUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
