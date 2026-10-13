namespace LegacyVault.Application.Security;

/// <summary>
/// Băm/kiểm tra mật khẩu app (SEC-01). Cài đặt ở Infrastructure.
/// </summary>
/// <remarks>
/// Cài đặt phải dùng Argon2id tối thiểu memory 19 MiB, iterations 2, parallelism 1, salt riêng mỗi mật khẩu;
/// lưu tham số kèm hash để sau tăng độ khó vẫn kiểm tra được hash cũ; so sánh thời gian cố định.
/// </remarks>
public interface IPasswordHasher
{
    string Hash(string password);

    bool Verify(string password, string passwordHash);
}
