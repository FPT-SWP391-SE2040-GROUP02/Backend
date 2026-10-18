using System.Security.Cryptography;
using LegacyVault.Application.Storage;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace LegacyVault.Infrastructure.Storage;

/// <summary>
/// Lưu nội dung tài sản ĐÃ MÃ HÓA thành tệp trên đĩa server (thư mục private, không phục vụ qua HTTP).
/// </summary>
/// <remarks>
/// Luồng ghi: tạo DEK mới → mã hóa từng khối (ChunkedAesGcm) vào tệp tạm → đổi tên tệp tạm thành tệp thật
/// → bọc DEK bằng KEK (DataKeyProtector) → trả StoredContent để service lưu vào DB.
/// Ghi vào tệp tạm rồi đổi tên, nên không bao giờ có object dở dang mang StorageKey thật.
/// Trên đĩa chỉ có ciphertext; lấy được tệp mà không có KEK thì không đọc được nội dung.
/// </remarks>
public sealed class LocalAssetContentStore : IAssetContentStore
{
    private const int BufferSize = 81_920;
    private readonly string rootPath;
    private readonly DataKeyProtector keyProtector;

    public LocalAssetContentStore(
        IOptions<AssetStorageSettings> options, IHostEnvironment environment, DataKeyProtector keyProtector)
    {
        this.rootPath = Path.GetFullPath(Path.Combine(environment.ContentRootPath, options.Value.RootPath));
        this.keyProtector = keyProtector;
    }

    public async Task<StoredContent> SaveAsync(Stream content, long maxBytes, CancellationToken ct)
    {
        string storageKey = Guid.CreateVersion7().ToString("N");
        string finalPath = PathFor(storageKey);
        string tempPath = finalPath + ".tmp";
        Directory.CreateDirectory(Path.GetDirectoryName(finalPath)!);

        byte[] dataKey = keyProtector.CreateDataKey();
        try
        {
            long size;
            string sha256Hex;
            using (IncrementalHash sha256 = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
            {
                try
                {
                    await using (FileStream output = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write,
                        FileShare.None, BufferSize, useAsync: true))
                    {
                        size = await ChunkedAesGcm.EncryptAsync(content, output, dataKey, maxBytes, sha256, ct);
                    }
                    File.Move(tempPath, finalPath);
                }
                catch
                {
                    File.Delete(tempPath);
                    throw;
                }
                sha256Hex = Convert.ToHexStringLower(sha256.GetHashAndReset());
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
        }
    }

    public async Task CopyToAsync(StoredContent content, Stream destination, CancellationToken ct)
    {
        byte[] dataKey = keyProtector.Unwrap(content.EncryptionKeyId, content.WrappedDataKey);
        try
        {
            long size;
            string sha256Hex;
            using (IncrementalHash sha256 = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
            {
                await using (FileStream input = new FileStream(PathFor(content.StorageKey), FileMode.Open,
                    FileAccess.Read, FileShare.Read, BufferSize, useAsync: true))
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

    public Task DeleteAsync(string storageKey, CancellationToken ct)
    {
        File.Delete(PathFor(storageKey));
        return Task.CompletedTask;
    }

    /// <summary>Chia thư mục con theo 2 ký tự cuối (phần ngẫu nhiên của Guid v7) để một thư mục không quá nhiều tệp.</summary>
    private string PathFor(string storageKey)
    {
        if (storageKey.Length != 32 || !storageKey.All(char.IsAsciiHexDigitLower))
        {
            throw new ArgumentException("StorageKey không hợp lệ.", nameof(storageKey));
        }
        return Path.Combine(rootPath, storageKey[^2..], storageKey);
    }
}
