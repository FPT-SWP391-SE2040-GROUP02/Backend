namespace LegacyVault.Application.Security;

/// <summary>
/// Băm/kiểm tra mã PIN của kho. Cài đặt ở Infrastructure.
/// </summary>
/// <remarks>
/// Đi qua interface riêng để đổi cách băm (ví dụ HMAC-SHA256 với pepper) chỉ là đổi một lớp cài đặt.
/// Đổi cách băm khi đã có dữ liệu thì mọi PIN cũ không kiểm tra được nữa, Owner phải đặt lại PIN.
/// </remarks>
public interface IVaultPinHasher
{
    string Hash(Guid vaultId, string pin);

    bool Verify(Guid vaultId, string pin, string pinHash);
}
