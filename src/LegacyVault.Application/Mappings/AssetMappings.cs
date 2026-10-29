using LegacyVault.Application.Dtos.Responses;
using LegacyVault.Application.Services;
using LegacyVault.Domain.Entities;

namespace LegacyVault.Application.Mappings;

public static class AssetMappings
{
    /// <summary>Không trả StorageKey (chi tiết lưu trữ nội bộ).</summary>
    public static AssetResponse ToResponse(this Asset asset)
    {
        return new AssetResponse
        {
            Id = asset.Id,
            VaultId = asset.VaultId,
            Kind = asset.Kind.ToApiString(),
            Name = asset.Name,
            Description = asset.Description,
            FileName = asset.FileName,
            ContentType = asset.ContentType,
            SizeBytes = asset.SizeBytes,
            CreatedAt = asset.CreatedAt,
            UpdatedAt = asset.UpdatedAt,
            Etag = Etag.From(asset.UpdatedAt),
        };
    }
}
