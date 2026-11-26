namespace LegacyVault.Application.Dtos.Responses;

/// <summary>Hồ sơ người nhận trả cho FE.</summary>
/// <remarks>DATA-02: KHÔNG có số CCCD đầy đủ, chỉ có 4 số cuối.</remarks>
public class BeneficiaryResponse
{
    public Guid Id { get; set; }

    public string CitizenId { get; set; } = string.Empty;

    public string FullName { get; set; } = string.Empty;

    public DateOnly DateOfBirth { get; set; }

    public string? Email { get; set; }

    public string? Phone { get; set; }

    /// <summary>Lời nhắn Chủ tài sản để lại cho người nhận; null khi chưa viết.</summary>
    public string? Message { get; set; }

    /// <summary>Id các tài sản đang được gán cho người này, theo thứ tự gán. Rỗng khi chưa được gán tài sản nào.</summary>
    public List<Guid> AssetIds { get; set; } = new List<Guid>();

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? UpdatedAt { get; set; }

    /// <summary>Gửi lại trong body (trường etag) khi sửa.</summary>
    public string Etag { get; set; } = string.Empty;

    /// <summary>True khi hồ sơ cùng CCCD đã tồn tại trong kho (tạo mới nhưng trả về hồ sơ cũ theo QUOTA-04).</summary>
    public bool IsExisting { get; set; }

    /// <summary>True khi máy chủ thư đã báo không giao được thư tới email này (bounce); Chủ tài sản nên sửa email.</summary>
    public bool EmailUndeliverable { get; set; }
}
