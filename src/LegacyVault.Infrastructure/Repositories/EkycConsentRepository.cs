using LegacyVault.Application.Repositories;
using LegacyVault.Domain.Entities;
using LegacyVault.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace LegacyVault.Infrastructure.Repositories;

/// <remarks>Chỉ là truy cập dữ liệu theo IEkycConsentRepository; luật nghiệp vụ ở EkycService.</remarks>
public sealed class EkycConsentRepository : IEkycConsentRepository
{
    private readonly AppDbContext context;

    public EkycConsentRepository(AppDbContext context)
    {
        this.context = context;
    }

    public Task<EkycConsent?> FindLatestByUserIdAsync(Guid userId, CancellationToken ct)
    {
        // ThenByDescending(Id): hai lần đồng ý cùng thời điểm vẫn có thứ tự cố định.
        return context.EkycConsents
            .AsNoTracking()
            .Where(c => c.UserId == userId)
            .OrderByDescending(c => c.ConsentedAt)
            .ThenByDescending(c => c.Id)
            .FirstOrDefaultAsync(ct);
    }

    public void Add(EkycConsent consent)
    {
        context.EkycConsents.Add(consent);
    }
}
