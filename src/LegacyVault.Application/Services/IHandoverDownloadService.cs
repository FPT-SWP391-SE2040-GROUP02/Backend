namespace LegacyVault.Application.Services;

/// <summary>Chuẩn bị việc tải một tài sản bàn giao của người nhận.</summary>
public interface IHandoverDownloadService
{
    /// <summary>
    /// Kiểm quyền, giải mã lượt 1 vào Stream.Null để xác nhận toàn vẹn rồi mới trả PreparedDownload.
    /// Nội dung hỏng → ContentIntegrityException và chưa có byte nội dung nào được gửi.
    /// </summary>
    Task<PreparedDownload> PrepareAsync(Guid grantId, Guid itemId, CancellationToken ct);
}
