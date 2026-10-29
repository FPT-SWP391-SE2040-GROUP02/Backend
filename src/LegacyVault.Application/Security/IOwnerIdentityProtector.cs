using LegacyVault.Domain.Entities;

namespace LegacyVault.Application.Security;

/// <summary>
/// Mã hóa/giải mã danh tính Owner (DATA-02, EKYC-27). Cài đặt ở Infrastructure.
/// </summary>
/// <remarks>
/// Có hai chỗ chứa bản mã hóa: danh tính đã gắn (OwnerIdentity) và bản tạm trên lượt eKYC đang chờ selfie
/// (EkycVerification.Enrollment*), vì giấy tờ và selfie nằm ở hai request khác nhau và FE không được gửi lại dữ liệu (EKYC-16).
/// </remarks>
public interface IOwnerIdentityProtector
{
    /// <summary>
    /// Tạo DEK MỚI, mã hóa danh tính rồi gán vào entity (EncryptionKeyId, WrappedDataKey, IdentityEncrypted,
    /// CitizenIdLast4).
    /// </summary>
    void Protect(OwnerIdentity identity, OwnerIdentityData data);

    /// <summary>Giải mã danh tính đã gắn. Dữ liệu bị sửa hoặc thiếu KEK → CryptographicException.</summary>
    OwnerIdentityData Unprotect(OwnerIdentity identity);

    /// <summary>Mã hóa danh tính tạm lên lượt eKYC (EnrollmentIdentityKeyId, EnrollmentWrappedDataKey, EnrollmentIdentityEncrypted).</summary>
    void ProtectPending(EkycVerification verification, OwnerIdentityData data);

    /// <summary>Giải mã danh tính tạm của lượt eKYC; lượt không có thì null. Dữ liệu bị sửa → CryptographicException.</summary>
    OwnerIdentityData? UnprotectPending(EkycVerification verification);
}
