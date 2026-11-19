namespace LegacyVault.Application.Security;

/// <summary>
/// Băm/kiểm tra mật khẩu app (SEC-01). Cài đặt ở Infrastructure.
/// </summary>
/// <remarks>
/// Cài đặt chính dùng PBKDF2-HMAC-SHA256 (pbkdf2$vòng$salt$hash) và vẫn nhận hash SHA-256 cũ salt$hash.
/// </remarks>
public interface IPasswordHasher
{
    string Hash(string password);

    bool Verify(string password, string passwordHash);

    /// <summary>true khi hash đang dùng định dạng cũ hoặc ít vòng hơn cấu hình: đăng nhập đúng thì băm lại và lưu.</summary>
    bool NeedsRehash(string passwordHash) => false;
}
