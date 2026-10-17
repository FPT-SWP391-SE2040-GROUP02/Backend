using LegacyVault.Domain.Entities;

namespace LegacyVault.Application.Repositories;

/// <summary>Truy cập bảng EkycConsents (việc đồng ý trước khi chụp giấy tờ, EKYC-03).</summary>
public interface IEkycConsentRepository
{
    /// <summary>Lần đồng ý mới nhất của người dùng, mọi mục đích. Chưa có → null.</summary>
    Task<EkycConsent?> FindLatestByUserIdAsync(Guid userId, CancellationToken ct);

    /// <summary>Đánh dấu "sẽ INSERT" một lần đồng ý.</summary>
    void Add(EkycConsent consent);
}
