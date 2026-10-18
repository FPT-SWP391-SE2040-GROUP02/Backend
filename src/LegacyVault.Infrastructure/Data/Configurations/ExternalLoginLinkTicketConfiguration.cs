using LegacyVault.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LegacyVault.Infrastructure.Data.Configurations;

public sealed class ExternalLoginLinkTicketConfiguration : IEntityTypeConfiguration<ExternalLoginLinkTicket>
{
    public void Configure(EntityTypeBuilder<ExternalLoginLinkTicket> builder)
    {
        builder.ToTable("ExternalLoginLinkTickets");

        builder.HasKey(t => t.Id);

        builder.Property(t => t.Provider)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(32);

        builder.Property(t => t.ProviderSubject)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(t => t.ProviderEmail)
            .HasMaxLength(256);

        // Vé chờ xác nhận mật khẩu khi email Google trùng tài khoản có sẵn; chỉ lưu mã băm của vé.
        builder.Property(t => t.TicketHash)
            .IsRequired()
            .HasMaxLength(64)
            .IsFixedLength();

        builder.HasIndex(t => t.TicketHash)
            .IsUnique();

        builder.HasOne(t => t.User)
            .WithMany()
            .HasForeignKey(t => t.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
