namespace LegacyVault.Infrastructure.Storage;

/// <summary>
/// Thao tác object tối thiểu trên bucket R2, để R2AssetContentStore không phụ thuộc trực tiếp thư viện S3
/// và test thay được bằng bản giả lập (không gọi R2 thật).
/// </summary>
public interface IR2ObjectClient
{
    /// <summary>Ghi object từ stream (có thể seek). Ghi đè nếu key đã có.</summary>
    Task PutAsync(string key, Stream content, CancellationToken ct);

    /// <summary>Mở stream đọc object; người gọi phải đóng stream. Không có object thì ném exception.</summary>
    Task<Stream> GetAsync(string key, CancellationToken ct);

    /// <summary>Xóa object; không có object thì bỏ qua.</summary>
    Task DeleteAsync(string key, CancellationToken ct);
}
