using LegacyVault.Domain.Entities;

namespace LegacyVault.Application.Security;

/// <summary>Bọc/mở bí mật TOTP bằng KEK của hệ thống. Cài đặt ở Infrastructure.</summary>
public interface ITotpSecretProtector
{
    /// <summary>Bọc bí mật rồi gán KeyId và WrappedSecret vào entity. Bí mật rõ không được lưu.</summary>
    void Protect(UserTotp totp, byte[] secret);

    /// <summary>Mở bí mật. Dữ liệu bị sửa hoặc thiếu KEK → CryptographicException.</summary>
    byte[] Unprotect(UserTotp totp);
}
