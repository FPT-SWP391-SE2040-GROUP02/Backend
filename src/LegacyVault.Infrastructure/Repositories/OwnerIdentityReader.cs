using LegacyVault.Application.Repositories;
using LegacyVault.Application.Security;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;
using LegacyVault.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace LegacyVault.Infrastructure.Repositories;

public sealed class OwnerIdentityReader : IOwnerIdentityReader
{
    private readonly AppDbContext context;
    private readonly IOwnerIdentityProtector identityProtector;

    public OwnerIdentityReader(AppDbContext context, IOwnerIdentityProtector identityProtector)
    {
        this.context = context;
        this.identityProtector = identityProtector;
    }

    public async Task<string?> FindBoundCitizenIdAsync(Guid userId, CancellationToken ct)
    {
        OwnerIdentity? identity = await context.OwnerIdentities
            .AsNoTracking()
            .Where(i => i.UserId == userId)
            .FirstOrDefaultAsync(ct);
        if (identity == null)
        {
            return null;
        }
        return identityProtector.Unprotect(identity).CitizenId;
    }

    public async Task<string?> FindVerifiedCitizenIdLast4Async(Guid userId, CancellationToken ct)
    {
        string? last4 = await context.Set<EkycVerification>()
            .Where(e => e.UserId == userId && e.Status == EkycStatus.Passed && e.CitizenIdLast4 != "")
            .OrderByDescending(e => e.CompletedAt)
            .ThenByDescending(e => e.Id)
            .Select(e => e.CitizenIdLast4)
            .FirstOrDefaultAsync(ct);
        return last4;
    }
}
