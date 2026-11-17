namespace LegacyVault.Domain.Enums;

/// <summary>
/// Mục đích của một phiên eKYC (SRS mục 4.1, EKYC-01). Quyết định so với ai và kết quả đạt dùng vào việc gì.
/// </summary>
public enum EkycPurpose
{
    /// <summary>Owner đăng ký: CCCD và selfie trực tiếp, gắn danh tính cho kho.</summary>
    OwnerEnrollment = 0,
}
