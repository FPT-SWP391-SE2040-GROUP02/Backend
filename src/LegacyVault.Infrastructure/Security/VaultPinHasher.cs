using LegacyVault.Application.Security;

namespace LegacyVault.Infrastructure.Security;

/// <summary>
/// Băm PIN kho bằng đúng bộ băm mật khẩu hiện có (<see cref="IPasswordHasher"/>: SHA-256 kèm salt riêng mỗi PIN).
/// </summary>
/// <remarks>
/// PIN 6 chữ số chỉ có 1.000.000 tổ hợp nên ai có DB đều dò ra được; PIN chặn người dùng tài khoản,
/// không bảo vệ nội dung mã hóa của tài sản. Muốn chặt hơn thì thay lớp này bằng HMAC-SHA256 với pepper
/// trong cấu hình (vaultId đã sẵn trong chữ ký hàm); khi đổi, mọi PIN cũ phải được đặt lại.
/// </remarks>
public sealed class VaultPinHasher : IVaultPinHasher
{
    private readonly IPasswordHasher passwordHasher;

    public VaultPinHasher(IPasswordHasher passwordHasher)
    {
        this.passwordHasher = passwordHasher;
    }

    public string Hash(Guid vaultId, string pin)
    {
        return passwordHasher.Hash(pin);
    }

    public bool Verify(Guid vaultId, string pin, string pinHash)
    {
        return passwordHasher.Verify(pin, pinHash);
    }
}
