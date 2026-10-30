using LegacyVault.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LegacyVault.Infrastructure.Data.Configurations;

public sealed class VaultPinConfiguration : IEntityTypeConfiguration<VaultPin>
{
    public void Configure(EntityTypeBuilder<VaultPin> builder)
    {
        builder.ToTable("VaultPins");

        // Mỗi kho có tối đa một PIN: khóa chính chính là VaultId.
        builder.HasKey(p => p.VaultId);

        // salt$hash dạng base64 (khoảng 70 ký tự); chừa dư cho cách băm khác.
        builder.Property(p => p.PinHash)
            .IsRequired()
            .HasMaxLength(256);

        // Nhiều request đoán PIN song song không được đếm sót lần sai.
        builder.Property(p => p.RowVersion)
            .IsRowVersion();

        builder.HasOne<Vault>()
            .WithOne()
            .HasForeignKey<VaultPin>(p => p.VaultId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
