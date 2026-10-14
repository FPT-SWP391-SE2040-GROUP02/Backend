namespace LegacyVault.Application.Services;

/// <summary>
/// Việc nền của giai đoạn nhận tài sản: chuyển lượt/grant quá hạn (TIME-05).
/// Mỗi hàm Process* đọc lại dữ liệu và kiểm lại điều kiện nên chạy lặp là an toàn.
/// </summary>
public interface IHandoverJob
{
    Task<IReadOnlyList<Guid>> FindExpiredResponseTurnIdsAsync(CancellationToken ct);

    Task<IReadOnlyList<Guid>> FindExpiredDecisionTurnIdsAsync(CancellationToken ct);

    Task<IReadOnlyList<Guid>> FindExpiredGrantIdsAsync(CancellationToken ct);

    /// <summary>Invited quá hạn phản hồi → InitialResponseExpired (vẫn nhận được lượt, CLAIM-02).</summary>
    Task ProcessResponseExpiryAsync(Guid turnId, CancellationToken ct);

    /// <summary>ReadyToAccept quá hạn quyết định → DecisionExpired; đóng kế hoạch nếu mọi lượt đã xong.</summary>
    Task ProcessDecisionExpiryAsync(Guid turnId, CancellationToken ct);

    /// <summary>Grant Active quá hạn → Expired.</summary>
    Task ProcessGrantExpiryAsync(Guid grantId, CancellationToken ct);
}
