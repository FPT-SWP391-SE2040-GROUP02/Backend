namespace LegacyVault.Domain.Enums;

/// <summary>
/// Kết quả một lượt eKYC của người dùng (bảng EkycVerifications).
/// </summary>
public enum EkycStatus
{
    /// <summary>Đã đọc mặt trước giấy tờ, đang chờ ảnh selfie để so khớp.</summary>
    Pending = 0,
    /// <summary>Selfie qua kiểm tra giả mạo và khớp khuôn mặt trên giấy tờ.</summary>
    Passed = 1,
    /// <summary>Selfie bị nghi giả mạo hoặc không khớp khuôn mặt trên giấy tờ.</summary>
    Failed = 2,
}
