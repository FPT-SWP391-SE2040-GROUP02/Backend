namespace LegacyVault.Domain.Enums;

/// <summary>
/// Mục đích của một phiên eKYC (SRS mục 4.1, EKYC-01). Quyết định so với ai và kết quả đạt dùng vào việc gì.
/// </summary>
public enum EkycPurpose
{
    /// <summary>Owner đăng ký: CCCD và selfie trực tiếp, gắn danh tính cho kho.</summary>
    OwnerEnrollment = 0,
    /// <summary>Người nhận: so CCCD với người được chỉ định trong snapshot (MATCH-01).</summary>
    BeneficiaryClaim = 1,
    /// <summary>Owner báo còn sống hoặc xin về Free: so selfie với mẫu khuôn mặt Owner.</summary>
    OwnerAlive = 2,
}
