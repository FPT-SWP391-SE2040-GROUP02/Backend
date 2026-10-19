namespace LegacyVault.Application.Dtos.Responses;

/// <summary>Hồ sơ người nhận trả cho FE.</summary>
/// <remarks>DATA-02: KHÔNG có số CCCD đầy đủ, chỉ có 4 số cuối.</remarks>
public class BeneficiaryResponse
{
    public Guid Id { get; set; }

    public string CitizenIdLast4 { get; set; } = string.Empty;

    public string FullName { get; set; } = string.Empty;

    public DateOnly DateOfBirth { get; set; }

    public string? Email { get; set; }

    public string? Phone { get; set; }

    /// <summary>Chưa đủ 18 tuổi tính theo ngày hiện tại ở Việt Nam. Chỉ để hiển thị, không ảnh hưởng quy tắc nào.</summary>
    public bool IsMinor { get; set; }

    /// <summary>Không còn dùng từ khi bỏ SETUP-19: hồ sơ mới luôn null. Giữ lại để FE cũ không vỡ.</summary>
    public DateTimeOffset? MinorAcknowledgedAt { get; set; }

    /// <summary>Id các gói đang chỉ định người này, theo thứ tự gắn. Rỗng khi chưa ở gói nào.</summary>
    public List<Guid> ContainerIds { get; set; } = new List<Guid>();

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? UpdatedAt { get; set; }

    /// <summary>Gửi lại trong body (trường etag) khi sửa.</summary>
    public string Etag { get; set; } = string.Empty;
}
