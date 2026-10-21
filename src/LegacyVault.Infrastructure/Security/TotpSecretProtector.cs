using System.Security.Cryptography;
using LegacyVault.Application.Security;
using LegacyVault.Domain.Entities;
using LegacyVault.Infrastructure.Storage;

namespace LegacyVault.Infrastructure.Security;

/// <summary>
/// Bọc bí mật TOTP (20 byte) bằng KEK của DataKeyProtector, giống cách bọc khóa dữ liệu của tài sản.
/// Cột lưu: KeyId của KEK + giá trị đã bọc (AES-GCM, có tag xác thực). Bí mật rõ chỉ tồn tại trong RAM lúc dùng.
/// </summary>
public sealed class TotpSecretProtector : ITotpSecretProtector
{
    private readonly DataKeyProtector dataKeyProtector;

    public TotpSecretProtector(DataKeyProtector dataKeyProtector)
    {
        this.dataKeyProtector = dataKeyProtector;
    }

    public void Protect(UserTotp totp, byte[] secret)
    {
        // DataKeyProtector.Wrap nhận khóa 32 byte; đệm bí mật 20 byte lên 32 byte rồi cắt lại khi mở.
        byte[] padded = new byte[DataKeyProtector.KeySize];
        Array.Copy(secret, padded, secret.Length);
        padded[DataKeyProtector.KeySize - 1] = (byte)secret.Length;
        try
        {
            WrappedDataKey wrapped = dataKeyProtector.Wrap(padded);
            totp.KeyId = wrapped.KeyId;
            totp.WrappedSecret = wrapped.Value;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(padded);
        }
    }

    public byte[] Unprotect(UserTotp totp)
    {
        byte[] padded = dataKeyProtector.Unwrap(totp.KeyId, totp.WrappedSecret);
        try
        {
            int length = padded[DataKeyProtector.KeySize - 1];
            if (length <= 0 || length >= DataKeyProtector.KeySize)
            {
                throw new CryptographicException("Bí mật TOTP không hợp lệ.");
            }
            byte[] secret = new byte[length];
            Array.Copy(padded, secret, length);
            return secret;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(padded);
        }
    }
}
