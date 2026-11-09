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
        DeathCase? deathCase = await context.Set<DeathCase>()
            .AsNoTracking()
            .Where(c => c.VaultId == vaultId && c.Status == DeathCaseStatus.ApprovedForHandover)
            .OrderByDescending(c => c.CreatedAt)
            .FirstOrDefaultAsync(ct);
        if (deathCase != null)
        {
            info.CaseCode = DeathCaseRules.CaseCode(deathCase.Id);
            var verifier = await context.Set<DeathCaseDecision>()
                .AsNoTracking()
                .Where(d => d.CaseId == deathCase.Id && d.Kind == DeathCaseDecisionKind.Approved)
                .OrderByDescending(d => d.DecidedAt)
                .Join(context.Set<User>(), d => d.VerifierUserId, u => u.Id,
                    (d, u) => new { u.FullName, u.LawyerCardNumber })
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
