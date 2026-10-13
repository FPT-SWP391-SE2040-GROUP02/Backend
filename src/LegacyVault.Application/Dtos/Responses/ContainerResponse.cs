namespace LegacyVault.Application.Dtos.Responses;

/// <summary>Thông tin một Container (gói bàn giao) trả cho FE.</summary>
public class ContainerResponse
{
    public Guid Id { get; set; }
    public Guid VaultId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }

    /// <summary>"DRAFT", "READY" hoặc "ARCHIVED" (SETUP-16).</summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>Id các tài sản trong gói. FE lấy tên/kích thước từ danh sách tài sản của kho.</summary>
    public List<Guid> AssetIds { get; set; } = new List<Guid>();

    /// <summary>Lý do còn DRAFT để FE hướng dẫn: "NO_ASSET", "NO_DESIGNATION". Rỗng khi READY/ARCHIVED.</summary>
    public List<string> MissingRequirements { get; set; } = new List<string>();

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public DateTimeOffset? ArchivedAt { get; set; }

    /// <summary>Gửi lại trong body (trường etag) khi đổi tên/mô tả.</summary>
    public string Etag { get; set; } = string.Empty;
}
