namespace LegacyVault.Application.Services;

/// <summary>Chính sách giai đoạn nhận tài sản đọc từ cấu hình (Application không tham chiếu IOptions).</summary>
public interface IHandoverPolicy
{
    /// <summary>true: người nhận phải qua xác minh danh tính (eKYC) mới được xem tài sản.</summary>
    bool RequireIdentityVerification { get; }
}
