using LegacyVault.Domain.Entities;

namespace LegacyVault.Application.Repositories;

/// <summary>Truy cập bảng DeathCaseObjections và dữ liệu F3 cần chặn/thu hồi khi Owner báo còn sống.</summary>
public interface IObjectionRepository
{
    /// <summary>Báo đang mở của hồ sơ (có theo dõi thay đổi); không có → null.</summary>
    Task<DeathCaseObjection?> FindOpenByCaseAsync(Guid caseId, CancellationToken ct);

    /// <summary>Báo gần nhất của hồ sơ, mở hay đã kết luận; không có → null.</summary>
    Task<DeathCaseObjection?> FindLatestByCaseAsync(Guid caseId, CancellationToken ct);

    void Add(DeathCaseObjection objection);

    /// <summary>Mọi lượt bàn giao của kế hoạch kèm grant (nếu có), có theo dõi thay đổi.</summary>
    Task<List<HandoverTurn>> FindTurnsWithGrantsAsync(Guid planId, CancellationToken ct);

    /// <summary>Lỗi lưu do trùng khóa unique (hai lần báo đầu tiên cùng lúc).</summary>
    bool IsUniqueViolation(Exception exception);
}
