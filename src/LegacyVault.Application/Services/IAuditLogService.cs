using LegacyVault.Application.Dtos.Requests;
using LegacyVault.Application.Dtos.Responses;

namespace LegacyVault.Application.Services;

/// <summary>
/// Owner xem nhật ký hoạt động của kho mình (SEC-13). Chỉ đọc: nhật ký không sửa, không xóa qua app (ADMIN-06, DATA-07).
/// </summary>
/// <remarks>
/// Lỗi chung: 401 SESSION_EXPIRED, 404 VAULT_NOT_FOUND (không có kho / kho người khác, SEC-04).
/// </remarks>
public interface IAuditLogService
{
    /// <summary>
    /// Một trang nhật ký của kho. sortBy chỉ nhận "at" (mặc định); isAscending=false để xem mới nhất trước.
    /// searchTerm không dùng. Lọc theo loại thao tác và khoảng thời gian [from, to).
    /// Lỗi: 400 VALIDATION_ERROR (sortBy lạ, action không có trong danh sách, from không trước to).
    /// </summary>
    Task<PaginatedList<AuditLogResponse>> GetVaultAuditLogsAsync(
        Guid vaultId, PaginationRequest pagination, AuditLogFilterRequest filter, CancellationToken ct);
}
