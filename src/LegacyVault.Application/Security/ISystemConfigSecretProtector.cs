using LegacyVault.Domain.Entities;

namespace LegacyVault.Application.Security;

/// <summary>
/// Mã hóa/giải mã các giá trị bí mật (khóa API, mật khẩu) của cấu hình dịch vụ ngoài bằng KEK của hệ thống.
/// Cài đặt ở Infrastructure.
/// </summary>
public interface ISystemConfigSecretProtector
{
    /// <summary>
    /// Tạo khóa dữ liệu MỚI, mã hóa toàn bộ bí mật rồi gán EncryptionKeyId, WrappedDataKey, SecretsEncrypted vào dòng.
    /// Không có bí mật nào thì để rỗng cả ba, không cần KEK.
    /// </summary>
    void Protect(SystemConfig row, IReadOnlyDictionary<string, string> secrets);

    /// <summary>Mở các bí mật của dòng. Dữ liệu bị sửa hoặc thiếu KEK → CryptographicException.</summary>
    Dictionary<string, string> Unprotect(SystemConfig row);
}
