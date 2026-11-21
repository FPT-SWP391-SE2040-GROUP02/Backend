using LegacyVault.Application.Notifications;
using LegacyVault.Application.Services;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;
using LegacyVault.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace LegacyVault.Infrastructure.Repositories;

/// <summary>Đọc họ tên Chủ kho và liên hệ Người thực thi cho thư mời nhận tài sản.</summary>
public sealed class HandoverContactReader : IHandoverContactReader
{
    private readonly AppDbContext context;

    public HandoverContactReader(AppDbContext context)
    {
        this.context = context;
    }

    public async Task<HandoverContactInfo> FindAsync(Guid vaultId, CancellationToken ct)
    {
        HandoverContactInfo info = new HandoverContactInfo();

        string? ownerName = await context.Set<Vault>()
            .AsNoTracking()
            .Where(v => v.Id == vaultId)
            .Select(v => v.Owner.FullName)
            .FirstOrDefaultAsync(ct);
        if (ownerName != null)
        {
            info.OwnerFullName = ownerName;
        }

        var executor = await context.Set<Executor>()
            .AsNoTracking()
            .Where(e => e.VaultId == vaultId && e.Status == ExecutorStatus.Accepted && e.UserId != null)
            .OrderBy(e => e.Id)
            .Select(e => new { e.User!.FullName, e.User.Phone, e.User.Email })
            .FirstOrDefaultAsync(ct);
        if (executor != null)
        {
            info.ExecutorFullName = executor.FullName;
            info.ExecutorPhone = executor.Phone;
            info.ExecutorEmail = executor.Email;
        }
        HandoverRecord? record = await context.Set<HandoverRecord>()
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.VaultId == vaultId, ct);
        if (record != null)
        {
            info.CaseCode = HandoverRecordRules.CaseCode(record.Id);
            var verifier = await context.Set<User>()
                .AsNoTracking()
                .Where(u => u.Id == record.VerifierUserId)
                .Select(u => new { u.FullName, u.LawyerCardNumber })
                .FirstOrDefaultAsync(ct);
            if (verifier != null)
            {
                info.VerifierFullName = verifier.FullName;
                info.VerifierLawyerCardNumber = verifier.LawyerCardNumber;
            }
        }
        return info;
    }
}
