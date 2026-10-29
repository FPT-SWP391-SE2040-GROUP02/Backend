using LegacyVault.Application.Storage;

namespace LegacyVault.IntegrationTests.TestSupport;

/// <summary>Kho nội dung giả trong RAM: lưu nguyên byte theo StorageKey, không mã hóa; đủ để kiểm ảnh được giữ hay xóa.</summary>
public sealed class InMemoryAssetContentStore : IAssetContentStore
{
    public Dictionary<string, byte[]> Contents { get; } = new Dictionary<string, byte[]>();

    public async Task<StoredContent> SaveAsync(Stream content, long maxBytes, CancellationToken ct)
    {
        using (MemoryStream buffer = new MemoryStream())
        {
            await content.CopyToAsync(buffer, ct);
            string storageKey = "test/" + Guid.NewGuid().ToString("N");
            Contents[storageKey] = buffer.ToArray();

            StoredContent stored = new StoredContent();
            stored.StorageKey = storageKey;
            stored.SizeBytes = buffer.Length;
            stored.EncryptionKeyId = "test-kek";
            stored.WrappedDataKey = new byte[60];
            return stored;
        }
    }

    public async Task CopyToAsync(StoredContent content, Stream destination, CancellationToken ct)
    {
        byte[] bytes = Contents[content.StorageKey];
        await destination.WriteAsync(bytes, ct);
    }

    public Task DeleteAsync(string storageKey, CancellationToken ct)
    {
        Contents.Remove(storageKey);
        return Task.CompletedTask;
    }
}
