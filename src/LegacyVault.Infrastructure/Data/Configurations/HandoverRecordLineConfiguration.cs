using LegacyVault.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LegacyVault.Infrastructure.Data.Configurations;

public sealed class HandoverRecordLineConfiguration : IEntityTypeConfiguration<HandoverRecordLine>
{
    public void Configure(EntityTypeBuilder<HandoverRecordLine> builder)
    {
        builder.ToTable("HandoverRecordLines");
        builder.HasKey(l => l.Id);

        // Một người nhận chỉ có một dòng cho mỗi tài sản; một tài sản có thể giao cho nhiều người (đồng thừa kế).
        builder.HasIndex(l => new { l.RecordId, l.BeneficiaryId, l.AssetId })
            .IsUnique();
        builder.HasIndex(l => l.BeneficiaryId);
    }
}
