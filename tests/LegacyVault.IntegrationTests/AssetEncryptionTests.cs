using System.Security.Cryptography;
using System.Text;
using LegacyVault.Application.Exceptions;
using LegacyVault.Application.Storage;
using LegacyVault.Infrastructure.Storage;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Xunit;

namespace LegacyVault.IntegrationTests;

/// <summary>
/// Mã hóa envelope nội dung tài sản: không cần SQL, ghi vào thư mục tạm riêng của mỗi test.
/// </summary>
public sealed class AssetEncryptionTests : IDisposable
{
    private const string KeyId = "test-key";
    private readonly string storageRoot;
    private readonly AssetEncryptionSettings settings;
    private readonly LocalAssetContentStore store;

    public AssetEncryptionTests()
    {
        storageRoot = Path.Combine(Path.GetTempPath(), $"legacyvault-enc-{Guid.NewGuid():N}");
        settings = SettingsWithKey(KeyId, RandomKeyBase64());
        store = CreateStore(settings);
    }

    public void Dispose()
    {
        if (Directory.Exists(storageRoot))
        {
            Directory.Delete(storageRoot, recursive: true);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(ChunkedAesGcm.ChunkSize - 1)]
    [InlineData(ChunkedAesGcm.ChunkSize)]
    [InlineData(ChunkedAesGcm.ChunkSize * 3 + 17)]
    public async Task SavedContentIsEncryptedAndDecryptsBackToOriginal(int length)
    {
        byte[] original = RandomBytes(length);

        StoredContent stored = await SaveAsync(original);

        Assert.Equal(length, stored.SizeBytes);
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(original)), stored.Sha256);
        Assert.Equal(KeyId, stored.EncryptionKeyId);
        Assert.Equal(DataKeyProtector.WrappedKeySize, stored.WrappedDataKey.Length);

        byte[] onDisk = await File.ReadAllBytesAsync(SingleStoredFile());
        Assert.Equal(ChunkedAesGcm.EncryptedSize(length), onDisk.Length);

        Assert.Equal(original, await ReadBackAsync(store, stored));
    }

    [Fact]
    public async Task FileOnDiskDoesNotContainPlainText()
    {
        byte[] secret = Encoding.UTF8.GetBytes("user: an / pass: bí-mật-rất-dài-để-tìm-trong-tệp");

        await SaveAsync(secret);

        byte[] onDisk = await File.ReadAllBytesAsync(SingleStoredFile());
        Assert.Equal(-1, onDisk.AsSpan().IndexOf(secret));
    }

    [Fact]
    public async Task EachVersionGetsItsOwnDataKey()
    {
        byte[] content = RandomBytes(100);

        StoredContent first = await SaveAsync(content);
        StoredContent second = await SaveAsync(content);

        Assert.NotEqual(first.WrappedDataKey, second.WrappedDataKey);
        byte[] firstFile = await File.ReadAllBytesAsync(PathOf(first.StorageKey));
        byte[] secondFile = await File.ReadAllBytesAsync(PathOf(second.StorageKey));
        Assert.NotEqual(firstFile, secondFile);
    }

    [Fact]
    public async Task TamperedCiphertextIsRejected()
    {
        StoredContent stored = await SaveAsync(RandomBytes(1000));
        string path = PathOf(stored.StorageKey);
        byte[] bytes = await File.ReadAllBytesAsync(path);
        bytes[ChunkedAesGcm.HeaderSize + 10] ^= 0x01;
        await File.WriteAllBytesAsync(path, bytes);

        await Assert.ThrowsAnyAsync<CryptographicException>(() => ReadBackAsync(store, stored));
    }

    [Fact]
    public async Task TruncatedFileIsRejected()
    {
        // Cắt đúng ở ranh giới khối: nếu không đánh dấu "khối cuối" thì tệp cụt vẫn giải mã được.
        StoredContent stored = await SaveAsync(RandomBytes(ChunkedAesGcm.ChunkSize * 2 + 5));
        string path = PathOf(stored.StorageKey);
        byte[] bytes = await File.ReadAllBytesAsync(path);
        int firstChunkEnd = ChunkedAesGcm.HeaderSize + ChunkedAesGcm.ChunkSize + ChunkedAesGcm.TagSize;
        await File.WriteAllBytesAsync(path, bytes.AsSpan(0, firstChunkEnd).ToArray());

        await Assert.ThrowsAnyAsync<CryptographicException>(() => ReadBackAsync(store, stored));
    }

    [Fact]
    public async Task WrongKeyEncryptionKeyCannotOpenContent()
    {
        StoredContent stored = await SaveAsync(RandomBytes(100));
        LocalAssetContentStore otherServer = CreateStore(SettingsWithKey(KeyId, RandomKeyBase64()));

        await Assert.ThrowsAnyAsync<CryptographicException>(() => ReadBackAsync(otherServer, stored));
    }

    [Fact]
    public async Task OldVersionsStillOpenAfterKeyRotation()
    {
        byte[] original = RandomBytes(500);
        StoredContent stored = await SaveAsync(original);

        AssetEncryptionSettings rotated = SettingsWithKey(KeyId, settings.Keys[KeyId]);
        rotated.Keys["new-key"] = RandomKeyBase64();
        rotated.ActiveKeyId = "new-key";
        LocalAssetContentStore rotatedStore = CreateStore(rotated);

        Assert.Equal(original, await ReadBackAsync(rotatedStore, stored));
        StoredContent newer = await rotatedStore.SaveAsync(new MemoryStream(original), long.MaxValue, CancellationToken.None);
        Assert.Equal("new-key", newer.EncryptionKeyId);
    }

    [Fact]
    public async Task ContentOverLimitIsRejectedAndLeavesNoFile()
    {
        AppException error = await Assert.ThrowsAsync<AppException>(() =>
            store.SaveAsync(new MemoryStream(RandomBytes(ChunkedAesGcm.ChunkSize + 1)), ChunkedAesGcm.ChunkSize,
                CancellationToken.None));

        Assert.Equal(ErrorCode.FileTooLarge, error.Code);
        Assert.Empty(Directory.GetFiles(storageRoot, "*", SearchOption.AllDirectories));
    }

    [Theory]
    [InlineData("", "AAAA")]
    [InlineData("Viết-Hoa", "AAAA")]
    [InlineData("ok", "chưa-phải-base64")]
    [InlineData("ok", "AAAAAAAAAAAAAAAAAAAAAA==")]
    public void InvalidKeyConfigurationIsReported(string keyId, string keyValue)
    {
        AssetEncryptionSettings invalid = new AssetEncryptionSettings();
        invalid.ActiveKeyId = keyId;
        invalid.Keys[keyId] = keyValue;

        Assert.NotNull(DataKeyProtector.FindConfigurationError(invalid));
    }

    [Fact]
    public void MissingActiveKeyIsReported()
    {
        AssetEncryptionSettings invalid = SettingsWithKey("old", RandomKeyBase64());
        invalid.ActiveKeyId = "new";

        Assert.NotNull(DataKeyProtector.FindConfigurationError(invalid));
        Assert.Null(DataKeyProtector.FindConfigurationError(settings));
    }

    private async Task<StoredContent> SaveAsync(byte[] content)
    {
        using (MemoryStream input = new MemoryStream(content))
        {
            return await store.SaveAsync(input, long.MaxValue, CancellationToken.None);
        }
    }

    private static async Task<byte[]> ReadBackAsync(LocalAssetContentStore source, StoredContent stored)
    {
        using (MemoryStream output = new MemoryStream())
        {
            await source.CopyToAsync(stored, output, CancellationToken.None);
            return output.ToArray();
        }
    }

    private LocalAssetContentStore CreateStore(AssetEncryptionSettings encryptionSettings)
    {
        AssetStorageSettings storageSettings = new AssetStorageSettings();
        storageSettings.RootPath = storageRoot;
        DataKeyProtector protector = new DataKeyProtector(Options.Create(encryptionSettings));
        return new LocalAssetContentStore(Options.Create(storageSettings), new TestHostEnvironment(), protector);
    }

    private string SingleStoredFile()
    {
        return Assert.Single(Directory.GetFiles(storageRoot, "*", SearchOption.AllDirectories));
    }

    private string PathOf(string storageKey)
    {
        return Path.Combine(storageRoot, storageKey.Substring(30), storageKey);
    }

    private static AssetEncryptionSettings SettingsWithKey(string keyId, string keyBase64)
    {
        AssetEncryptionSettings result = new AssetEncryptionSettings();
        result.ActiveKeyId = keyId;
        result.Keys[keyId] = keyBase64;
        return result;
    }

    private static string RandomKeyBase64()
    {
        return Convert.ToBase64String(RandomNumberGenerator.GetBytes(DataKeyProtector.KeySize));
    }

    private static byte[] RandomBytes(int length)
    {
        byte[] bytes = new byte[length];
        Random.Shared.NextBytes(bytes);
        return bytes;
    }

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Test";
        public string ApplicationName { get; set; } = "LegacyVault.Tests";
        public string ContentRootPath { get; set; } = Path.GetTempPath();
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
