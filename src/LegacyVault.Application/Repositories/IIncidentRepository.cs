using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;

namespace LegacyVault.Application.Repositories;

/// <summary>Truy cập bảng Incidents và IncidentEvents. Hàm Find trả entity có theo dõi thay đổi.</summary>
public interface IIncidentRepository
{
    void Add(Incident incident);

    void AddEvent(IncidentEvent incidentEvent);

    /// <summary>Sự cố chi tiết kèm lịch sử xử lý.</summary>
    Task<Incident?> FindByIdAsync(Guid incidentId, CancellationToken ct);

    /// <summary>Sự cố chưa đóng của đúng (loại, đối tượng); không có → null.</summary>
    Task<Incident?> FindOpenAsync(IncidentType type, string targetType, Guid? targetId, CancellationToken ct);

    Task<(List<Incident> Items, int TotalCount)> ListAsync(
        IncidentStatus? status, IncidentType? type, Guid? assigneeUserId, bool overdueOnly, DateTimeOffset now,
        int pageIndex, int pageSize, CancellationToken ct);

    /// <summary>Id sự cố chưa đóng cần job xử lý: quá hạn nhắc, quá 72 giờ chưa báo Admin, hoặc chưa có người phụ trách.</summary>
    Task<List<Guid>> FindIdsForJobAsync(DateTimeOffset now, int maxCount, CancellationToken ct);

    Task<int> CountOpenAsync(CancellationToken ct);

    Task<int> CountOverdueAsync(DateTimeOffset now, CancellationToken ct);

    Task<int> CountUnassignedAsync(CancellationToken ct);

    /// <summary>Admin đang hoạt động có ít sự cố chưa đóng nhất (hòa theo Id); không có Admin → null.</summary>
    Task<Guid?> FindLeastLoadedAdminAsync(CancellationToken ct);

    bool IsUniqueViolation(Exception exception);
}
