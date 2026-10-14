using LegacyVault.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LegacyVault.Infrastructure.Data.Configurations;

public sealed class CheckInScheduleConfiguration : IEntityTypeConfiguration<CheckInSchedule>
{
    public void Configure(EntityTypeBuilder<CheckInSchedule> builder)
    {
        builder.ToTable("CheckInSchedules");

        builder.HasKey(s => s.Id);

        // ETag của cấu hình điểm danh (UpdateCheckInSettingsRequest.Etag).
        builder.Property(s => s.UpdatedAt)
            .IsConcurrencyToken();

        // Mỗi kế hoạch có đúng một lịch (SETUP-13).
        builder.HasIndex(s => s.PlanId)
            .IsUnique();

        builder.HasOne(s => s.Plan)
            .WithMany()
            .HasForeignKey(s => s.PlanId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
