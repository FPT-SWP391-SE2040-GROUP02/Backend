using LegacyVault.Application.Repositories;
using LegacyVault.Domain.Entities;
using LegacyVault.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace LegacyVault.Infrastructure.Repositories;

/// <remarks>Chỉ là truy cập dữ liệu theo IEkycVerificationRepository; luật nghiệp vụ ở EkycService.</remarks>
public sealed class EkycVerificationRepository : IEkycVerificationRepository
{
    private readonly AppDbContext context;

    public EkycVerificationRepository(AppDbContext context)
    {
        this.context = context;
    }

    public Task<EkycVerification?> FindLatestByUserIdAsync(Guid userId, CancellationToken ct)
    {
        // ThenByDescending(Id): hai lượt cùng thời điểm vẫn có thứ tự cố định.
        return context.EkycVerifications
            .Where(v => v.UserId == userId)
            .OrderByDescending(v => v.CreatedAt)
            .ThenByDescending(v => v.Id)
            .FirstOrDefaultAsync(ct);
    }

    public void Add(EkycVerification verification)
    {
        context.EkycVerifications.Add(verification);
    }
}
