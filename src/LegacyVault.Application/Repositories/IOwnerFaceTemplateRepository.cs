using LegacyVault.Domain.Entities;

namespace LegacyVault.Application.Repositories;

/// <summary>Truy cập bảng OwnerFaceTemplates (mẫu khuôn mặt Owner, EKYC-22, EKYC-29).</summary>
public interface IOwnerFaceTemplateRepository
{
    /// <summary>Mẫu đang dùng của Owner (phiên bản lớn nhất). Chưa có → null.</summary>
    Task<OwnerFaceTemplate?> FindLatestByUserIdAsync(Guid userId, CancellationToken ct);

    /// <summary>Đánh dấu "sẽ INSERT" một mẫu mới.</summary>
    void Add(OwnerFaceTemplate template);
}
