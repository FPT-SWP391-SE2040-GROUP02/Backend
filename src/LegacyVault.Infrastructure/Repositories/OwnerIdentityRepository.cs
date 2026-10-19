using LegacyVault.Application.Repositories;
using LegacyVault.Domain.Entities;
using LegacyVault.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace LegacyVault.Infrastructure.Repositories;

/// <remarks>Chỉ là truy cập dữ liệu theo IOwnerIdentityRepository; luật nghiệp vụ ở EkycService.</remarks>
public sealed class OwnerIdentityRepository : IOwnerIdentityRepository
{
    private readonly AppDbContext context;

    public OwnerIdentityRepository(AppDbContext context)
    {
        this.context = context;
    }

    public Task<OwnerIdentity?> FindByUserIdAsync(Guid userId, CancellationToken ct)
    {
        return context.OwnerIdentities
            .AsNoTracking()
            .Where(i => i.UserId == userId)
            .FirstOrDefaultAsync(ct);
    }

    public void Add(OwnerIdentity identity)
    {
        context.OwnerIdentities.Add(identity);
    }
}
