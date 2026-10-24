using LegacyVault.Domain.Entities;

namespace LegacyVault.Application.Repositories;

/// <summary>
/// Truy cập các bảng bàn giao: HandoverTurns, HandoverSnapshotItems, HandoverGrants, HandoverAccessLogs.
/// </summary>
/// <remarks>
/// Repository chỉ đánh dấu Add/sửa; service gọi IUnitOfWork.SaveChangesAsync một lần ở cuối use case (STATE-02).
/// Các hàm "có theo dõi" trả entity mà sửa xong rồi SaveChanges là lưu.
/// </remarks>
public interface IHandoverRepository
{
    /// <summary>Lượt theo mã băm token trong link mời, kèm Items và Grant (có theo dõi). Không có → null.</summary>
    Task<HandoverTurn?> FindTurnByTokenHashAsync(string tokenHash, CancellationToken ct);

    /// <summary>Lượt theo Id, kèm Items và Grant (có theo dõi). Không có → null.</summary>
    Task<HandoverTurn?> FindTurnByIdAsync(Guid turnId, CancellationToken ct);

    /// <summary>Các lượt mà tài khoản đã nhận, kèm Items và Grant, mới nhất (InvitedAt giảm dần) trước.</summary>
    Task<List<HandoverTurn>> FindTurnsByClaimantAsync(Guid userId, CancellationToken ct);

    /// <summary>Mọi lượt của một kế hoạch (có theo dõi), để kiểm tra khi nào kế hoạch đóng.</summary>
    Task<List<HandoverTurn>> FindTurnsByPlanAsync(Guid planId, CancellationToken ct);

    /// <summary>Grant theo Id, kèm Turn và Turn.Items (có theo dõi). Không có → null.</summary>
    Task<HandoverGrant?> FindGrantByIdAsync(Guid grantId, CancellationToken ct);

    /// <summary>
    /// Các chỉ định đủ điều kiện để mở bàn giao của một kho: gói đang READY (chưa lưu trữ), kèm
    /// Container.Assets.Asset và Beneficiary.
    /// </summary>
    Task<List<Designation>> FindStartableDesignationsAsync(Guid vaultId, CancellationToken ct);

    /// <summary>Một tài sản theo Id (không lọc theo kho). Không có → null.</summary>
    Task<Asset?> FindAssetAsync(Guid assetId, CancellationToken ct);

    /// <summary>Id các lượt Invited đã quá hạn phản hồi (ResponseDeadline &lt;= now), hạn sớm nhất trước.</summary>
    Task<IReadOnlyList<Guid>> FindExpiredResponseTurnIdsAsync(DateTimeOffset now, CancellationToken ct);

    /// <summary>
    /// Lượt đang chờ phản hồi mà lời mời chưa từng được giao (InvitationSentAt null), tạo trước createdBefore
    /// và chưa đủ maxAttempts lần thử. Dùng để gửi lại tự động khi lần gửi đầu lỗi hoặc tiến trình dừng giữa chừng.
    /// </summary>
    Task<IReadOnlyList<Guid>> FindUndeliveredInvitationTurnIdsAsync(
        DateTimeOffset createdBefore, int maxAttempts, CancellationToken ct);

    /// <summary>Id các lượt ReadyToAccept đã quá hạn quyết định (DecisionDeadline &lt;= now).</summary>
    Task<IReadOnlyList<Guid>> FindExpiredDecisionTurnIdsAsync(DateTimeOffset now, CancellationToken ct);

    /// <summary>Id các grant Active đã quá hạn (ExpiresAt &lt;= now).</summary>
    Task<IReadOnlyList<Guid>> FindExpiredGrantIdsAsync(DateTimeOffset now, CancellationToken ct);

    /// <summary>
    /// true nếu lỗi lưu là do vi phạm chỉ mục unique (ví dụ hai lần "đồng ý" song song cùng tạo grant cho một lượt).
    /// Giúp Application nhận ra tình huống này mà không phải biết exception của EF/SQL Server.
    /// </summary>
    bool IsUniqueViolation(Exception exception);

    void AddTurn(HandoverTurn turn);

    void AddGrant(HandoverGrant grant);

    void AddAccessLog(HandoverAccessLog log);
}
