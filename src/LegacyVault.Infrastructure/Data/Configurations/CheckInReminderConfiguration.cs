using LegacyVault.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LegacyVault.Infrastructure.Data.Configurations;

public sealed class CheckInReminderConfiguration : IEntityTypeConfiguration<CheckInReminder>
{
    public void Configure(EntityTypeBuilder<CheckInReminder> builder)
    {
        builder.ToTable("CheckInReminders");

        builder.HasKey(r => r.Id);

        builder.Property(r => r.Kind)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(32);

        builder.Property(r => r.Status)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(32);

        // Mỗi mốc của mỗi kỳ chỉ một dòng: chặn gửi trùng ở tầng DB (TIME-02).
        builder.HasIndex(r => new { r.PeriodId, r.Kind })
            .IsUnique();

        builder.HasOne(r => r.Period)
            .WithMany(p => p.Reminders)
            .HasForeignKey(r => r.PeriodId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
