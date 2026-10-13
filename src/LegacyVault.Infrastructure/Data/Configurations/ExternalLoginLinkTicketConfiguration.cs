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
        builder.Property(t => t.Provider).HasConversion<string>().HasMaxLength(32);
        builder.Property(t => t.ProviderSubject).HasMaxLength(255).IsRequired();
        builder.Property(t => t.ProviderEmail).HasMaxLength(320);
        builder.Property(t => t.TicketHash).HasMaxLength(64).IsRequired();
        builder.HasIndex(t => t.TicketHash).IsUnique();
        builder.HasOne(t => t.User).WithMany().HasForeignKey(t => t.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}
