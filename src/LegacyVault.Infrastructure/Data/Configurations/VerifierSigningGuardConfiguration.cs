using LegacyVault.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LegacyVault.Infrastructure.Data.Configurations;

public sealed class VerifierSigningGuardConfiguration : IEntityTypeConfiguration<VerifierSigningGuard>
{
    public void Configure(EntityTypeBuilder<VerifierSigningGuard> builder)
    {
        builder.ToTable("VerifierSigningGuards");

        // DEATH-14: mỗi Verifier một bộ đếm: khóa chính chính là UserId.
        builder.HasKey(g => g.UserId);

        // Nhiều request đoán mật khẩu ký song song không được đếm sót lần sai.
        builder.Property(g => g.RowVersion)
            .IsRowVersion();

        builder.HasOne<User>()
            .WithOne()
            .HasForeignKey<VerifierSigningGuard>(g => g.UserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
