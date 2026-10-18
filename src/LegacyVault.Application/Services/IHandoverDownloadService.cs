namespace LegacyVault.Application.Services;

/// <summary>Một lượt tải đã qua kiểm quyền và kiểm toàn vẹn, sẵn sàng ghi ra response.</summary>
public sealed class PreparedDownload
{
    public string FileName { get; set; } = string.Empty;

    public string ContentType { get; set; } = string.Empty;

    public long Length { get; set; }

    /// <summary>Giải mã lần hai và ghi thẳng vào luồng đích (response body).</summary>
    public Func<Stream, CancellationToken, Task> WriteToAsync { get; set; } = (stream, token) => Task.CompletedTask;
}

public interface IHandoverDownloadService
{
    /// <summary>
    /// Kiểm quyền, giải mã lượt 1 vào Stream.Null để xác nhận toàn vẹn rồi mới trả PreparedDownload.
    /// Nội dung hỏng → ContentIntegrityException và chưa có byte nội dung nào được gửi.
    /// </summary>
    Task<PreparedDownload> PrepareAsync(Guid grantId, Guid itemId, CancellationToken ct);
}
