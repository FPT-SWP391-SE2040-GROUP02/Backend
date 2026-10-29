using LegacyVault.Domain.Entities;

namespace LegacyVault.Application.Security;

/// <summary>
/// Mã hóa/giải mã dữ liệu cá nhân của hồ sơ người nhận (DATA-02, §11.6). Cài đặt ở Infrastructure.
/// </summary>
public interface IBeneficiaryDataProtector
{
    /// <summary>
    /// Tạo DEK MỚI, mã hóa phần định danh và phần liên hệ rồi gán vào entity
    /// (EncryptionKeyId, WrappedDataKey, IdentityEncrypted, ContactEncrypted, CitizenIdLast4).
    /// </summary>
    void Protect(Beneficiary beneficiary, BeneficiaryPersonalData data);

    /// <summary>Giải mã dữ liệu của hồ sơ. Dữ liệu bị sửa hoặc thiếu KEK → CryptographicException.</summary>
    BeneficiaryPersonalData Unprotect(Beneficiary beneficiary);
}
