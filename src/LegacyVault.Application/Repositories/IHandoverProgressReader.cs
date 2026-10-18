using LegacyVault.Domain.Entities;

namespace LegacyVault.Application.Repositories;

/// <summary>
/// Đọc dữ liệu cho màn hình Executor theo dõi tiến độ bàn giao. Chỉ đọc, không theo dõi thay đổi.
/// </summary>
/// <remarks>
/// Tách khỏi IHandoverRepository để không sửa interface dùng chung của phần bàn giao.
/// Kiểm tra quyền nằm ở HandoverProgressService.
/// </remarks>
public interface IHandoverProgressReader
{
    /// <summary>Hồ sơ chứng tử theo id, không kèm phiên bản hay tệp; null khi không có.</summary>
    Task<DeathCase?> FindCaseAsync(Guid caseId, CancellationToken ct);

    /// <summary>Các lượt bàn giao của kế hoạch, kèm danh sách tài sản đã ghim và quyền tải.</summary>
    Task<List<HandoverTurn>> FindTurnsWithItemsAsync(Guid planId, CancellationToken ct);

    /// <summary>
    /// Lần tải thành công đầu tiên của từng tài sản trong các quyền tải cho trước.
    /// Khóa là id tài sản đã ghim; tài sản chưa tải thành công lần nào thì không có trong kết quả.
    /// </summary>
    Task<Dictionary<Guid, DateTimeOffset>> FindFirstDownloadTimesAsync(List<Guid> grantIds, CancellationToken ct);

    /// <summary>4 số cuối CCCD của các người nhận cho trước, theo id người nhận.</summary>
    Task<Dictionary<Guid, string>> FindCitizenIdLast4Async(List<Guid> beneficiaryIds, CancellationToken ct);
}
