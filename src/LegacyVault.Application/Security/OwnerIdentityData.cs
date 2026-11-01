namespace LegacyVault.Application.Security;

/// <summary>Danh tính Owner đọc từ giấy tờ, dạng đã giải mã. Chỉ tồn tại trong RAM; DB chỉ giữ bản mã hóa (DATA-02).</summary>
public class OwnerIdentityData
{
    /// <summary>Đủ 12 chữ số, giữ số 0 đầu.</summary>
    public string CitizenId { get; set; } = string.Empty;

    public string FullName { get; set; } = string.Empty;

    public DateOnly DateOfBirth { get; set; }

    /// <summary>Nơi cư trú người dùng đã đối chiếu ở bước xác nhận bản khai. Rỗng khi chưa xác nhận hoặc danh tính gắn trước khi có trường này.</summary>
    public string ResidenceAddress { get; set; } = string.Empty;
}
