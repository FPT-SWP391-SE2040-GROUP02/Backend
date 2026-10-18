using System.Net;
using System.Security.Cryptography;
using Amazon.S3;
using LegacyVault.Application.Storage;
using LegacyVault.Infrastructure.Storage;
using Microsoft.Extensions.Options;
using Xunit;

namespace LegacyVault.IntegrationTests;

/// <summary>
/// Lưu nội dung tài sản lên R2 bằng bản giả lập trong bộ nhớ; không gọi Cloudflare thật.
/// </summary>
public sealed class R2AssetContentStoreTests
{
    private readonly FakeR2ObjectClient objectClient = new FakeR2ObjectClient();
    private readonly R2AssetContentStore store;

    public R2AssetContentStoreTests()
    {
        AssetEncryptionSettings settings = new AssetEncryptionSettings();
        settings.ActiveKeyId = "test-key";
        settings.Keys["test-key"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        DataKeyProtector protector = new DataKeyProtector(Options.Create(settings));
        store = new R2AssetContentStore(objectClient, protector);
    }

    [Fact]
    public async Task SaveThenCopy_ReturnsOriginalContent_AndObjectHoldsCiphertextOnly()
    {
        byte[] plain = RandomNumberGenerator.GetBytes(300_000);

        StoredContent stored = await store.SaveAsync(new MemoryStream(plain), long.MaxValue, CancellationToken.None);

        Assert.Equal(plain.Length, stored.SizeBytes);
        Assert.Single(objectClient.Objects);
        byte[] cipher = objectClient.Objects[stored.StorageKey];
        Assert.False(cipher.AsSpan().IndexOf(plain.AsSpan(0, 64)) >= 0);

        MemoryStream output = new MemoryStream();
        await store.CopyToAsync(stored, output, CancellationToken.None);
        Assert.Equal(plain, output.ToArray());
    }

    [Fact]
    public async Task Copy_WhenObjectIsTampered_Throws()
    {
        StoredContent stored = await store.SaveAsync(
            new MemoryStream(RandomNumberGenerator.GetBytes(1000)), long.MaxValue, CancellationToken.None);
        byte[] cipher = objectClient.Objects[stored.StorageKey];
        cipher[cipher.Length / 2] ^= 0xFF;

        await Assert.ThrowsAnyAsync<CryptographicException>(
            () => store.CopyToAsync(stored, new MemoryStream(), CancellationToken.None));
    }

    [Fact]
    public async Task Delete_RemovesObject()
    {
        StoredContent stored = await store.SaveAsync(
            new MemoryStream(new byte[] { 1, 2, 3 }), long.MaxValue, CancellationToken.None);

        await store.DeleteAsync(stored.StorageKey, CancellationToken.None);

        Assert.Empty(objectClient.Objects);
    }

    [Fact]
    public async Task Delete_WithInvalidKey_Throws()
    {
        await Assert.ThrowsAsync<ArgumentException>(
            () => store.DeleteAsync("../etc/passwd", CancellationToken.None));
    }

    [Fact]
    public async Task Copy_WhenObjectIsMissing_ThrowsIoExceptionForIntegrityPath()
    {
        StoredContent stored = await store.SaveAsync(
            new MemoryStream(new byte[] { 1, 2, 3 }), long.MaxValue, CancellationToken.None);
        objectClient.Objects.Remove(stored.StorageKey);

        // DOWNLOAD-05: service bắt IOException để báo lỗi toàn vẹn, không phải 503 "thử lại sau".
        await Assert.ThrowsAnyAsync<IOException>(
            () => store.CopyToAsync(stored, new MemoryStream(), CancellationToken.None));
    }

    [Fact]
    public void IsMissingObject_OnlyNoSuchKeyCounts()
    {
        AmazonS3Exception missingKey = new AmazonS3Exception("missing");
        missingKey.ErrorCode = "NoSuchKey";
        missingKey.StatusCode = HttpStatusCode.NotFound;
        AmazonS3Exception missingBucket = new AmazonS3Exception("missing");
        missingBucket.ErrorCode = "NoSuchBucket";
        missingBucket.StatusCode = HttpStatusCode.NotFound;

        Assert.True(S3R2ObjectClient.IsMissingObject(missingKey));
        Assert.False(S3R2ObjectClient.IsMissingObject(missingBucket));
    }

    [Fact]
    public void R2Settings_MissingValues_ReportError()
    {
        R2StorageSettings settings = new R2StorageSettings();
        Assert.NotNull(settings.FindError());

        settings.AccountId = "a";
        settings.BucketName = "b";
        settings.AccessKeyId = "c";
        settings.SecretAccessKey = "d";
        Assert.Null(settings.FindError());
    }

    private sealed class FakeR2ObjectClient : IR2ObjectClient
    {
        public Dictionary<string, byte[]> Objects { get; } = new Dictionary<string, byte[]>();

        public async Task PutAsync(string key, Stream content, CancellationToken ct)
        {
            MemoryStream copy = new MemoryStream();
            await content.CopyToAsync(copy, ct);
            Objects[key] = copy.ToArray();
        }

        /// <summary>Không có object thì ném FileNotFoundException, giống S3R2ObjectClient khi R2 trả NoSuchKey.</summary>
        public Task<Stream> GetAsync(string key, CancellationToken ct)
        {
            byte[]? content;
            if (!Objects.TryGetValue(key, out content))
            {
                throw new FileNotFoundException("Không tìm thấy object.");
            }
            return Task.FromResult<Stream>(new MemoryStream(content, writable: false));
        }

        public Task DeleteAsync(string key, CancellationToken ct)
        {
            Objects.Remove(key);
            return Task.CompletedTask;
        }
    }
}
