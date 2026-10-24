namespace LegacyVault.Application.Dtos.Responses;

/// <summary>Metadata một tài sản trả cho FE. KHÔNG chứa nội dung tài sản.</summary>
public class AssetResponse
{
    public Guid Id { get; set; }
    public Guid VaultId { get; set; }

    /// <summary>"FILE" hoặc "TEXT".</summary>
    public string Kind { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }

    /// <summary>Tên tệp gốc; null với tài sản TEXT.</summary>
    public string? FileName { get; set; }

    public string ContentType { get; set; } = string.Empty;
    public long SizeBytes { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }

    /// <summary>Gửi lại trong body (trường etag) khi sửa tài sản.</summary>
    public string Etag { get; set; } = string.Empty;
}
