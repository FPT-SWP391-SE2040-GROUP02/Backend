using LegacyVault.Application.Dtos.Requests;
using LegacyVault.Application.Dtos.Responses;
using LegacyVault.Application.Exceptions;
using LegacyVault.Application.Mappings;
using LegacyVault.Application.Repositories;
using LegacyVault.Application.Security;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;

namespace LegacyVault.Application.Services;

public sealed class AuditLogService(
    ICurrentUser currentUser,
    IVaultRepository vaultRepository,
    IAuditLogRepository auditLogRepository) : IAuditLogService
{
    private const string SortByAt = "at";

    public async Task<PaginatedList<AuditLogResponse>> GetVaultAuditLogsAsync(
        Guid vaultId, PaginationRequest pagination, AuditLogFilterRequest filter, CancellationToken ct)
    {
        // SEC-04, SEC-13: chỉ Owner xem nhật ký kho mình; kho người khác cũng 404 để không lộ sự tồn tại.
        Guid userId = currentUser.RequireUserId();
        Vault vault = VaultRules.EnsureOwnedBy(await vaultRepository.FindByIdAsync(vaultId, ct), userId);

        if (!string.IsNullOrWhiteSpace(pagination.SortBy)
            && !string.Equals(pagination.SortBy, SortByAt, StringComparison.OrdinalIgnoreCase))
        {
            throw ValidationErrors.For("sortBy", "Chỉ sắp xếp theo at.");
        }
        AuditAction? action = ParseAction(filter.Action);
        if (filter.From != null && filter.To != null && filter.From.Value >= filter.To.Value)
        {
            throw ValidationErrors.For("from", "Thời điểm bắt đầu phải trước thời điểm kết thúc.");
        }

        (List<AuditLog> Items, int TotalCount) page = await auditLogRepository.FindPageByVaultIdAsync(
            vault.Id, action, filter.From, filter.To, pagination.PageIndex, pagination.PageSize,
            pagination.IsAscending, ct);

        List<AuditLogResponse> responses = new List<AuditLogResponse>();
        foreach (AuditLog log in page.Items)
        {
            responses.Add(log.ToResponse());
        }
        return responses.ToPaginatedList(page.TotalCount, pagination);
    }

    /// <summary>
    /// Đổi chuỗi UPPER_SNAKE_CASE FE gửi (ví dụ CONTAINER_CREATED) sang enum; trống → null (mọi loại).
    /// Không dùng Enum.Parse thẳng để FE không gửi được số hay tên C# (đúng contract chuỗi).
    /// </summary>
    private static AuditAction? ParseAction(string? action)
    {
        string? text = TextInput.NullIfBlank(action);
        if (text == null)
        {
            return null;
        }
        foreach (AuditAction value in Enum.GetValues<AuditAction>())
        {
            if (string.Equals(value.ToApiString(), text, StringComparison.Ordinal))
            {
                return value;
            }
        }
        throw ValidationErrors.For("action", "Loại thao tác không hợp lệ.");
    }
}
