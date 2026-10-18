using LegacyVault.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LegacyVault.Infrastructure.Data.Configurations;

public sealed class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> builder)
    {
        builder.ToTable("RefreshTokens");

        builder.HasKey(t => t.Id);

        // SHA-256 dạng hex luôn đủ 64 ký tự; chỉ mục duy nhất để tìm nhanh token trong cookie.
        builder.Property(t => t.TokenHash)
            .IsRequired()
            .HasMaxLength(64)
            .IsFixedLength();

        builder.HasIndex(t => t.TokenHash)
            .IsUnique();

        // Thu hồi cả một lần đăng nhập (mọi token cùng FamilyId) khi phát hiện token cũ bị dùng lại hoặc đăng xuất.
        builder.HasIndex(t => t.FamilyId);

        builder.HasOne(t => t.User)
            .WithMany()
            .HasForeignKey(t => t.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(t => t.UserId);
    }
}
