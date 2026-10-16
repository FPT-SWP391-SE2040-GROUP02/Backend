using LegacyVault.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LegacyVault.Infrastructure.Data.Configurations;

public sealed class IncidentConfiguration : IEntityTypeConfiguration<Incident>
{
    public void Configure(EntityTypeBuilder<Incident> builder)
    {
        builder.ToTable("Incidents");
        builder.HasKey(i => i.Id);

        builder.Property(i => i.Type).IsRequired().HasConversion<string>().HasMaxLength(32);
        builder.Property(i => i.Status).IsRequired().HasConversion<string>().HasMaxLength(32);
        builder.Property(i => i.TargetType).IsRequired().HasMaxLength(32);
        builder.Property(i => i.Summary).IsRequired().HasMaxLength(500);
        builder.Property(i => i.ResolutionNote).HasMaxLength(1000);
        builder.Property(i => i.UpdatedAt).IsConcurrencyToken();

        // Một sự cố đang mở cho mỗi (loại, đối tượng): job chạy lặp hay chạy song song không tạo trùng.
        builder.HasIndex(i => new { i.Type, i.TargetType, i.TargetId })
            .IsUnique()
            .HasFilter("[Status] <> 'Resolved'");
        builder.HasIndex(i => new { i.AssigneeUserId, i.Status });
        builder.HasIndex(i => new { i.Status, i.DueAt });

        builder.HasMany(i => i.Events)
            .WithOne()
            .HasForeignKey(e => e.IncidentId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class IncidentEventConfiguration : IEntityTypeConfiguration<IncidentEvent>
{
    public void Configure(EntityTypeBuilder<IncidentEvent> builder)
    {
        builder.ToTable("IncidentEvents");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Kind).IsRequired().HasConversion<string>().HasMaxLength(32);
        builder.Property(e => e.Note).HasMaxLength(1000);
        builder.HasIndex(e => new { e.IncidentId, e.At });
    }
}
