using LegacyVault.Domain.Entities;

namespace LegacyVault.Application.Repositories;

/// <summary>Truy cập bảng WillVersions (các phiên bản di chúc số đã ký).</summary>
public interface IWillVersionRepository
{
    /// <summary>Phiên bản ký gần nhất của kho (VersionNo lớn nhất). Chưa có → null.</summary>
    Task<WillVersion?> FindLatestByVaultIdAsync(Guid vaultId, CancellationToken ct);

    /// <summary>Đánh dấu "sẽ INSERT" một phiên bản; service lưu cùng thao tác chính.</summary>
    void Add(WillVersion version);
}
