using System.Security.Cryptography;
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

        public Task<Stream> GetAsync(string key, CancellationToken ct)
        {
            return Task.FromResult<Stream>(new MemoryStream(Objects[key], writable: false));
        }

        public Task DeleteAsync(string key, CancellationToken ct)
        {
            Objects.Remove(key);
            return Task.CompletedTask;
        }
    }
}
