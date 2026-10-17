namespace LegacyVault.Application.Repositories;

/// <summary>Tìm hồ sơ chứng tử mà Executor đã quá hạn bổ sung (DEATH-01).</summary>
public interface ISupplementOverdueRepository
{
    /// <summary>
    /// Id hồ sơ SUPPLEMENT_REQUIRED chưa được ghi nhận quá hạn mà yêu cầu bổ sung mới nhất của bản đang xét đã tới hạn.
    /// Cũ nhất trước.
    /// </summary>
    Task<List<Guid>> FindOverdueCaseIdsAsync(DateTimeOffset now, int maxCount, CancellationToken ct);
}
