using System.Security.Cryptography;
using LegacyVault.Application.Storage;

namespace LegacyVault.Infrastructure.Storage;

/// <summary>
/// Lưu nội dung tài sản ĐÃ MÃ HÓA lên bucket Cloudflare R2 private.
/// </summary>
/// <remarks>
/// Cùng luồng mã hóa với LocalAssetContentStore: tạo DEK → mã hóa từng khối (ChunkedAesGcm) vào tệp tạm →
/// tải tệp tạm lên R2 → bọc DEK bằng KEK → trả StoredContent. R2 chỉ giữ ciphertext; không có KEK thì không đọc được.
/// Tệp tạm nằm ở thư mục tạm của hệ điều hành và luôn bị xóa sau khi tải lên (hoặc khi lỗi).
/// </remarks>
public sealed class R2AssetContentStore : IAssetContentStore
{
    private const int BufferSize = 81_920;
    private readonly IR2ObjectClient objectClient;
    private readonly DataKeyProtector keyProtector;

    public R2AssetContentStore(IR2ObjectClient objectClient, DataKeyProtector keyProtector)
    {
        this.objectClient = objectClient;
        this.keyProtector = keyProtector;
    }

    public async Task<StoredContent> SaveAsync(Stream content, long maxBytes, CancellationToken ct)
    {
        string storageKey = Guid.CreateVersion7().ToString("N");
        string tempPath = Path.Combine(Path.GetTempPath(), "lv-" + storageKey + ".tmp");
        byte[] dataKey = keyProtector.CreateDataKey();
        try
        {
            long size;
            string sha256Hex;
            using (IncrementalHash sha256 = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
            {
                await using (FileStream output = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write,
                    FileShare.None, BufferSize, useAsync: true))
                {
                    size = await ChunkedAesGcm.EncryptAsync(content, output, dataKey, maxBytes, sha256, ct);
                }
                sha256Hex = Convert.ToHexStringLower(sha256.GetHashAndReset());
            }

            await using (FileStream upload = new FileStream(tempPath, FileMode.Open, FileAccess.Read,
                FileShare.Read, BufferSize, useAsync: true))
            {
                await objectClient.PutAsync(storageKey, upload, ct);
            }

            WrappedDataKey wrapped = keyProtector.Wrap(dataKey);
            StoredContent stored = new StoredContent();
            stored.StorageKey = storageKey;
            stored.SizeBytes = size;
            stored.Sha256 = sha256Hex;
            stored.EncryptionKeyId = wrapped.KeyId;
            stored.WrappedDataKey = wrapped.Value;
            return stored;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(dataKey);
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }

    public async Task CopyToAsync(StoredContent content, Stream destination, CancellationToken ct)
    {
        ValidateKey(content.StorageKey);
        byte[] dataKey = keyProtector.Unwrap(content.EncryptionKeyId, content.WrappedDataKey);
        try
        {
            long size;
            string sha256Hex;
            using (IncrementalHash sha256 = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
            {
                await using (Stream input = await objectClient.GetAsync(content.StorageKey, ct))
                {
                    size = await ChunkedAesGcm.DecryptAsync(input, destination, dataKey, sha256, ct);
                }
                sha256Hex = Convert.ToHexStringLower(sha256.GetHashAndReset());
            }

            if (size != content.SizeBytes || sha256Hex != content.Sha256)
            {
                throw new CryptographicException("Nội dung giải mã không khớp kích thước/SHA-256 đã lưu.");
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(dataKey);
        }
    }

    public async Task DeleteAsync(string storageKey, CancellationToken ct)
    {
        ValidateKey(storageKey);
        await objectClient.DeleteAsync(storageKey, ct);
    }

    /// <summary>StorageKey do server tự sinh (Guid v7 dạng hex); từ chối mọi giá trị khác để không gọi R2 với key lạ.</summary>
    private static void ValidateKey(string storageKey)
    {
        if (storageKey.Length != 32 || !storageKey.All(char.IsAsciiHexDigitLower))
        {
            throw new ArgumentException("StorageKey không hợp lệ.", nameof(storageKey));
        }
    }
}
