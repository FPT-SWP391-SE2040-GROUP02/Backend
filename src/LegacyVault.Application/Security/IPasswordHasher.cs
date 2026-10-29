namespace LegacyVault.Application.Security;

/// <summary>
/// Băm/kiểm tra mật khẩu app (SEC-01). Cài đặt ở Infrastructure.
/// </summary>
/// <remarks>
/// Cài đặt dùng SHA-256 với salt ngẫu nhiên riêng mỗi mật khẩu; lưu dạng salt$hash.
/// </remarks>
public interface IPasswordHasher
{
    string Hash(string password);

    bool Verify(string password, string passwordHash);
}
