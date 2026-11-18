namespace LegacyVault.Application.Services;

/// <summary>Người thực thi tải ảnh biên bản đồng ý hoặc văn bản từ chối đã duyệt của một lượt.</summary>
public interface IExecutorHandoverPaperService
{
    /// <summary>Người thực thi xem ảnh văn bản từ chối (lượt bị từ chối) hoặc biên bản đồng ý (lượt đã đồng ý nhận).</summary>
    Task<PreparedDownload> PrepareAsync(Guid caseId, Guid turnId, CancellationToken ct);
}
