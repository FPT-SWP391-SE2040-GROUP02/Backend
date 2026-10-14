using LegacyVault.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LegacyVault.Infrastructure.Data.Configurations;

public sealed class ExecutorConfiguration : IEntityTypeConfiguration<Executor>
{
    public void Configure(EntityTypeBuilder<Executor> builder)
    {
        builder.ToTable("Executors");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.InviteeEmail)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(e => e.Role)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(32);

        builder.Property(e => e.Status)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(32);

        builder.Property(e => e.InvitationTokenHash)
            .IsRequired()
            .HasMaxLength(64)
            .IsFixedLength();

        // Chấp nhận, thu hồi và việc hết hạn có thể cùng sửa một dòng;
        // UpdatedAt khác đi nghĩa là thao tác kia vừa lưu trước.
        builder.Property(e => e.UpdatedAt)
            .IsConcurrencyToken();

        // Tìm lời mời theo token trong link; token trùng là lỗi nên chặn ngay ở database.
        builder.HasIndex(e => e.InvitationTokenHash)
            .IsUnique();

        // Liệt kê Executor của kho và đọc các Executor đang hiệu lực.
        builder.HasIndex(e => new { e.VaultId, e.Status });

        // Tìm các kho mà một tài khoản đang làm Executor.
        builder.HasIndex(e => e.UserId);

        // Không xóa dây chuyền: lời mời là lịch sử, giữ lại dù kho, tài khoản hay hồ sơ người nhận thay đổi.
        builder.HasOne(e => e.Vault)
            .WithMany()
            .HasForeignKey(e => e.VaultId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.User)
            .WithMany()
            .HasForeignKey(e => e.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.Beneficiary)
            .WithMany()
            .HasForeignKey(e => e.BeneficiaryId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
