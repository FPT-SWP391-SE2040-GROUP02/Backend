using LegacyVault.Application.Repositories;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;
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

    public Task<EkycVerification?> FindLatestByUserAndPurposeAsync(Guid userId, EkycPurpose purpose, CancellationToken ct)
    {
        return context.EkycVerifications
            .AsNoTracking()
            .Where(v => v.UserId == userId && v.Purpose == purpose)
            .OrderByDescending(v => v.CreatedAt)
            .ThenByDescending(v => v.Id)
            .FirstOrDefaultAsync(ct);
    }

    public Task<EkycVerification?> FindLatestPassedByUserAndPurposeAsync(Guid userId, EkycPurpose purpose, CancellationToken ct)
    {
        return context.EkycVerifications
            .AsNoTracking()
            .Where(v => v.UserId == userId && v.Purpose == purpose && v.Status == EkycStatus.Passed)
            .OrderByDescending(v => v.CreatedAt)
            .ThenByDescending(v => v.Id)
            .FirstOrDefaultAsync(ct);
    }

    public Task<EkycVerification?> FindLatestByHandoverTurnAsync(Guid userId, Guid handoverTurnId, CancellationToken ct)
    {
        return context.EkycVerifications
            .AsNoTracking()
            .Where(v => v.UserId == userId && v.HandoverTurnId == handoverTurnId)
            .OrderByDescending(v => v.CreatedAt)
            .ThenByDescending(v => v.Id)
            .FirstOrDefaultAsync(ct);
    }

    public void Add(EkycVerification verification)
    {
        context.EkycVerifications.Add(verification);
    }
}
