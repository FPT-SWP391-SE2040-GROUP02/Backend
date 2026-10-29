using LegacyVault.Domain.Entities;

namespace LegacyVault.Application.Security;

/// <summary>
/// Mã hóa/giải mã phần khai báo nhạy cảm của phiên bản hồ sơ chứng tử (DATA-02, D6). Cài đặt ở Infrastructure.
/// </summary>
public interface IDeathCaseDataProtector
{
    /// <summary>
    /// Tạo DEK MỚI, mã hóa họ tên Owner, CCCD Owner, số giấy, nơi cấp thành một gói rồi gán vào phiên bản
    /// (EncryptionKeyId, WrappedDataKey, IdentityEncrypted, OwnerCitizenIdLast4).
    /// Mọi trường đều null thì xóa các cột mã hóa (bản nháp chưa nhập gì).
    /// </summary>
    void Protect(DeathCaseVersion version, DeathCaseIdentityData data);

    /// <summary>
    /// Giải mã phần khai báo; phiên bản chưa có dữ liệu mã hóa → null.
    /// Dữ liệu bị sửa hoặc thiếu KEK → CryptographicException.
    /// </summary>
    DeathCaseIdentityData? Unprotect(DeathCaseVersion version);
}
