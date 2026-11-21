using LegacyVault.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LegacyVault.Infrastructure.Data.Configurations;

public sealed class HandoverRecordSignatureConfiguration : IEntityTypeConfiguration<HandoverRecordSignature>
{
    public void Configure(EntityTypeBuilder<HandoverRecordSignature> builder)
    {
        builder.ToTable("HandoverRecordSignatures");
        builder.HasKey(s => s.Id);

        builder.Property(s => s.Role)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(16);

        builder.HasIndex(s => new { s.RecordId, s.RevisionNo });
        // Mỗi bản biên bản chỉ có một chữ ký cho mỗi bên (người nhận tính theo từng người): bấm ký hai lần cùng lúc
        // thì lần sau vi phạm unique và trả 409 thay vì ghi trùng chữ ký.
        builder.HasIndex(s => new { s.RecordId, s.RevisionNo, s.Role, s.BeneficiaryId })
            .IsUnique()
            .HasFilter(null);
    }
}
