using LegacyVault.Application.Repositories;
using LegacyVault.Domain.Entities;
using LegacyVault.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace LegacyVault.Infrastructure.Repositories;

/// <remarks>Chỉ là truy cập dữ liệu theo IOwnerFaceTemplateRepository; luật nghiệp vụ ở EkycService.</remarks>
public sealed class OwnerFaceTemplateRepository : IOwnerFaceTemplateRepository
{
    private readonly AppDbContext context;

    public OwnerFaceTemplateRepository(AppDbContext context)
    {
        this.context = context;
    }

    public Task<OwnerFaceTemplate?> FindLatestByUserIdAsync(Guid userId, CancellationToken ct)
    {
        return context.OwnerFaceTemplates
            .AsNoTracking()
            .Where(t => t.UserId == userId)
            .OrderByDescending(t => t.Version)
            .ThenByDescending(t => t.Id)
            .FirstOrDefaultAsync(ct);
    }

    public void Add(OwnerFaceTemplate template)
    {
        context.OwnerFaceTemplates.Add(template);
    }
}
