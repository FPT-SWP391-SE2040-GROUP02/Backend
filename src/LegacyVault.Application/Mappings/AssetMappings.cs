using LegacyVault.Application.Dtos.Responses;
using LegacyVault.Application.Services;
using LegacyVault.Domain.Entities;

namespace LegacyVault.Application.Mappings;

public static class AssetMappings
{
    /// <summary>Cần asset đã nạp Versions. Không trả StorageKey/Sha256 (chi tiết lưu trữ nội bộ).</summary>
    public static AssetResponse ToResponse(this Asset asset)
    {
        var current = asset.Versions.Single(v => v.VersionNumber == asset.CurrentVersionNumber);
        return new AssetResponse
        {
            Id = asset.Id,
            VaultId = asset.VaultId,
            Kind = asset.Kind.ToApiString(),
            Name = asset.Name,
            Description = asset.Description,
            CurrentVersion = new AssetVersionResponse
            {
                VersionNumber = current.VersionNumber,
                FileName = current.FileName,
                ContentType = current.ContentType,
                SizeBytes = current.SizeBytes,
                CreatedAt = current.CreatedAt,
            },
            CreatedAt = asset.CreatedAt,
            UpdatedAt = asset.UpdatedAt,
            Etag = Etag.From(asset.UpdatedAt),
        };
    }
}
