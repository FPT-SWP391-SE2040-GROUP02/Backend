using LegacyVault.Domain.Enums;

namespace LegacyVault.Application.Services;

/// <summary>
/// Việc nền gửi thông báo hồ sơ chứng tử cho Owner theo outbox (DEATH-08, D3).
/// ProcessNoticeAsync đọc lại dữ liệu và kiểm lại trạng thái nên chạy lặp là an toàn (TIME-02).
/// </summary>
public interface IDeathCaseNoticeJob
{
    /// <summary>Id các thông báo còn Pending, cũ nhất trước (giới hạn số dòng mỗi vòng quét).</summary>
    Task<List<Guid>> FindPendingNoticeIdsAsync(CancellationToken ct);

    /// <summary>
    /// Gửi một thông báo Pending. Trả trạng thái thông báo sau khi xử lý (Pending nếu lỗi và còn lượt thử,
    /// Delivered, Failed); null khi bỏ qua (không có, hoặc không còn Pending).
    /// </summary>
    Task<DeathCaseNoticeStatus?> ProcessNoticeAsync(Guid noticeId, CancellationToken ct);
}
