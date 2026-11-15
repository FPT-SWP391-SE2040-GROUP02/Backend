using System.Security.Cryptography;
using System.Text;
using LegacyVault.Application.Security;
using LegacyVault.Domain.Entities;
using LegacyVault.Infrastructure.Storage;

namespace LegacyVault.Infrastructure.Security;

/// <summary>
/// Mã hóa envelope mảnh khóa Người thực thi, dùng lại DataKeyProtector của tài sản.
/// </summary>
public sealed class ExecutorKeyProtector : IExecutorKeyProtector
{
    private readonly DataKeyProtector dataKeyProtector;

    public ExecutorKeyProtector(DataKeyProtector dataKeyProtector)
    {
        this.dataKeyProtector = dataKeyProtector;
    }

    public void Protect(ExecutorKeyShard shard, string plainShard)
    {
        if (string.IsNullOrWhiteSpace(plainShard))
        {
            shard.EncryptedShard = null;
            shard.EncryptionKeyId = null;
            shard.WrappedDataKey = null;
            return;
        }

        byte[] dataKey = dataKeyProtector.CreateDataKey();
        byte[] plainBytes = Encoding.UTF8.GetBytes(plainShard);
        try
        {
            WrappedDataKey wrapped = dataKeyProtector.Wrap(dataKey);
            byte[] cipherBytes = AesGcmCipher.Encrypt(dataKey, plainBytes);

            shard.EncryptedShard = Convert.ToBase64String(cipherBytes);
            shard.EncryptionKeyId = wrapped.KeyId;
            shard.WrappedDataKey = wrapped.Value;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(dataKey);
            CryptographicOperations.ZeroMemory(plainBytes);
        }
    }

    public string Unprotect(ExecutorKeyShard shard)
    {
        if (string.IsNullOrEmpty(shard.EncryptedShard) ||
            shard.WrappedDataKey == null ||
            string.IsNullOrEmpty(shard.EncryptionKeyId))
        {
            return string.Empty;
        }

        byte[] dataKey = dataKeyProtector.Unwrap(shard.EncryptionKeyId, shard.WrappedDataKey);
        byte[] cipherBytes = Convert.FromBase64String(shard.EncryptedShard);
        byte[]? plainBytes = null;
        try
        {
            plainBytes = AesGcmCipher.Decrypt(dataKey, cipherBytes);
            return Encoding.UTF8.GetString(plainBytes);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(dataKey);
            if (plainBytes != null)
            {
                CryptographicOperations.ZeroMemory(plainBytes);
            }
        }
    }
}
