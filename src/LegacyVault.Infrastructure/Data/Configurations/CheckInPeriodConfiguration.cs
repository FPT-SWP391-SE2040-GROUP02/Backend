using LegacyVault.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LegacyVault.Infrastructure.Data.Configurations;

public sealed class CheckInPeriodConfiguration : IEntityTypeConfiguration<CheckInPeriod>
{
    public void Configure(EntityTypeBuilder<CheckInPeriod> builder)
    {
        builder.ToTable("CheckInPeriods");

        builder.HasKey(p => p.Id);

        // Điểm danh và job cùng sửa dòng này; RowVersion lệch nghĩa là bên kia vừa ghi trước.
        builder.Property(p => p.RowVersion)
            .IsRowVersion();

        builder.Property(p => p.StartedByExecutor)
            .HasDefaultValue(false);

        // Kỳ mở trước khi có cấu hình thư nhắc trễ dùng mặc định 3 thư, cách nhau 3 ngày.
        builder.Property(p => p.LateReminderGapDays)
            .HasDefaultValue(3);

        builder.Property(p => p.MaxLateReminders)
            .HasDefaultValue(3);

        builder.HasIndex(p => new { p.PlanId, p.Sequence })
            .IsUnique();

        // Mỗi kế hoạch tối đa một kỳ đang mở (ClosedAt IS NULL).
        builder.HasIndex(p => p.PlanId)
            .IsUnique()
            .HasFilter("[ClosedAt] IS NULL")
            .HasDatabaseName("UX_CheckInPeriods_OpenPerPlan");

        builder.HasIndex(p => p.DueAt);

        builder.HasOne(p => p.Plan)
            .WithMany()
            .HasForeignKey(p => p.PlanId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
