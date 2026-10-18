using LegacyVault.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LegacyVault.Infrastructure.Data.Configurations;

public sealed class HandoverTurnConfiguration : IEntityTypeConfiguration<HandoverTurn>
{
    public void Configure(EntityTypeBuilder<HandoverTurn> builder)
    {
        builder.ToTable("HandoverTurns");

        builder.HasKey(t => t.Id);

        builder.Property(t => t.ContainerName)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(t => t.Status)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(32);

        builder.Property(t => t.RetentionStatus)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(32);

        // Job lưu dữ liệu tìm các lượt chưa dọn theo kế hoạch.
        builder.HasIndex(t => new { t.PlanId, t.RetentionStatus });

        // SHA-256 dạng hex chữ thường: chỉ ký tự ASCII, đúng 64 ký tự.
        builder.Property(t => t.InvitationTokenHash)
            .IsRequired()
            .HasMaxLength(64)
            .IsFixedLength()
            .IsUnicode(false);

        // Hai thao tác cùng sửa một lượt (đồng ý, hết hạn, nhận lượt): bên lưu sau bị báo xung đột.
        builder.Property(t => t.RowVersion)
            .IsRowVersion();

        // Tìm lượt theo token trong link; token trùng là lỗi nên chặn ở DB.
        builder.HasIndex(t => t.InvitationTokenHash)
            .IsUnique();

        // DATA-04: mỗi chỉ định chỉ một lượt đang hiệu lực hoặc đã nhận.
        builder.HasIndex(t => t.DesignationId)
            .IsUnique()
            .HasFilter("[Status] IN ('Invited','InitialResponseExpired','IdentityInProgress','ReadyToAccept','HandoverCommitted')")
            .HasDatabaseName("UX_HandoverTurns_ActivePerDesignation");

        builder.HasIndex(t => new { t.ClaimantUserId, t.Status });
        builder.HasIndex(t => new { t.Status, t.ResponseDeadline });
        builder.HasIndex(t => new { t.Status, t.DecisionDeadline });
        builder.HasIndex(t => t.PlanId);

        // Giữ lịch sử: không xóa dây chuyền.
        builder.HasMany(t => t.Items)
            .WithOne(i => i.Turn)
            .HasForeignKey(i => i.TurnId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(t => t.Grant)
            .WithOne(g => g.Turn)
            .HasForeignKey<HandoverGrant>(g => g.TurnId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
