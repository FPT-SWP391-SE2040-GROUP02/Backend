using LegacyVault.Domain.Entities;

namespace LegacyVault.Application.Repositories;

/// <summary>Truy cập thư bổ sung của Admin và dấu đã gửi theo kỳ điểm danh.</summary>
public interface IExtraMailRepository
{
    Task<List<ExtraMailRule>> ListAsync(CancellationToken ct);

    /// <summary>Luật đang bật; không theo dõi thay đổi.</summary>
    Task<List<ExtraMailRule>> ListEnabledAsync(CancellationToken ct);

    /// <summary>Theo dõi thay đổi để sửa tại chỗ; không có thì null.</summary>
    Task<ExtraMailRule?> FindAsync(Guid id, CancellationToken ct);

    Task<int> CountAsync(CancellationToken ct);

    void Add(ExtraMailRule rule);

    void Remove(ExtraMailRule rule);

    /// <summary>Id các kỳ điểm danh đang mở, có hạn không muộn hơn horizon, kế hoạch đang ACTIVE hoặc CHECKIN_OVERDUE.</summary>
    Task<IReadOnlyList<Guid>> FindOpenPeriodIdsAsync(DateTimeOffset horizon, CancellationToken ct);

    /// <summary>Kỳ kèm kế hoạch, kho và Chủ kho.</summary>
    Task<CheckInPeriod?> FindPeriodAsync(Guid periodId, CancellationToken ct);

    /// <summary>Id các luật đã xử lý cho kỳ này.</summary>
    Task<List<Guid>> ListHandledRuleIdsAsync(Guid periodId, CancellationToken ct);

    void AddSend(ExtraMailSend send);
}
