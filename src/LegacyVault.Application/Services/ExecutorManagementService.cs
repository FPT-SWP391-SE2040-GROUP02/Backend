using LegacyVault.Application.Dtos;
using LegacyVault.Application.Exceptions;
using LegacyVault.Application.Mappings;
using LegacyVault.Application.Repositories;
using LegacyVault.Application.Security;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;

namespace LegacyVault.Application.Services;

public interface IExecutorManagementService
{
    /// <summary>
    /// Owner thu hồi lời mời hoặc Executor (SETUP-06). Gọi lặp trên dòng đã kết thúc trả trạng thái hiện tại.
    /// Lỗi: 404 VAULT_NOT_FOUND / EXECUTOR_NOT_FOUND, 409 EDIT_CONFLICT (etag cũ), 409 VAULT_NOT_EDITABLE (mất quyền quản lý),
    /// 409 EXECUTOR_NOT_ALLOWED reason REPLACEMENT_REQUIRED / BACKUP_EXECUTOR_REQUIRED.
    /// </summary>
    Task<ExecutorInvitationResponse> RevokeAsync(Guid vaultId, Guid executorId, ResendExecutorRequest request, CancellationToken ct);
}

/// <remarks>
/// - SETUP-21: chỉ khi còn quyền quản lý (kho mở, kế hoạch không ở DEATH_CASE_OPEN / HANDOVER_IN_PROGRESS / BLOCKED / CLOSED).
/// - Kế hoạch đang chạy không được bỏ trống Executor chính (ASSIGN-05): thu hồi Executor chính đã chấp nhận phải đi qua
///   "mời người thay" (PlanSetupService.InviteAsync); người cũ tự thôi khi người mới chấp nhận.
/// - Kế hoạch đang chạy mà Executor chính cũng là người nhận thì không được bỏ Executor dự phòng độc lập cuối cùng (SETUP-10).
/// - Chạy trong giao dịch Serializable như các thao tác Executor khác của PlanSetupService.
/// </remarks>
public sealed class ExecutorManagementService(ICurrentUser currentUser, IVaultRepository vaults,
    IUnitOfWork unitOfWork, TimeProvider time) : IExecutorManagementService
{
    public Task<ExecutorInvitationResponse> RevokeAsync(
        Guid vaultId, Guid executorId, ResendExecutorRequest request, CancellationToken ct) =>
        unitOfWork.ExecuteSerializableAsync(async () =>
        {
            Vault vault = VaultRules.EnsureOwnedBy(await vaults.FindByIdAsync(vaultId, ct), currentUser.RequireUserId());
            VaultRules.EnsureCanReduce(vault);
            Executor item = vault.Executors.SingleOrDefault(e => e.Id == executorId)
                ?? throw new AppException(AppErrorType.NotFound, ErrorCode.ExecutorNotFound, "Không tìm thấy Executor.");

            DateTimeOffset now = time.GetUtcNow();
            bool live = item.Status == ExecutorStatus.Accepted || item.Status == ExecutorStatus.Suspended
                || (item.Status == ExecutorStatus.Invited && item.ExpiresAt > now);
            if (!live)
            {
                return Map(item, now);
            }
            if (!Etag.Matches(request.Etag, item.UpdatedAt))
            {
                throw new AppException(AppErrorType.Conflict, ErrorCode.EditConflict,
                    "Lời mời đã thay đổi. Tải lại trước khi thu hồi.");
            }

            if (item.Status == ExecutorStatus.Accepted && PlanRules.IsRunning(vault.Plan.Status))
            {
                if (item.Role == ExecutorRole.Primary)
                {
                    throw NotAllowed("REPLACEMENT_REQUIRED");
                }
                Executor? primary = vault.Executors.FirstOrDefault(
                    e => e.Role == ExecutorRole.Primary && e.Status == ExecutorStatus.Accepted);
                bool needsBackup = primary != null && primary.IsBeneficiary;
                bool otherBackup = vault.Executors.Any(e => e.Id != item.Id && e.Role == ExecutorRole.Backup
                    && e.Status == ExecutorStatus.Accepted && !e.IsBeneficiary);
                if (needsBackup && !otherBackup)
                {
                    throw NotAllowed("BACKUP_EXECUTOR_REQUIRED");
                }
            }

            item.Status = ExecutorStatus.Revoked;
            item.EndedAt = now;
            item.UpdatedAt = now;
            PlanRules.BumpConfigVersion(vault.Plan, now);
            await unitOfWork.SaveChangesAsync(ct);
            return Map(item, now);
        }, ct);

    private static ExecutorInvitationResponse Map(Executor item, DateTimeOffset now) => new(item.Id,
        item.Role.ToApiString(),
        item.Status == ExecutorStatus.Invited && item.ExpiresAt <= now ? "EXPIRED" : item.Status.ToApiString(),
        item.InviteeEmail, item.IsBeneficiary, item.ExpiresAt, item.AcceptedAt, item.InvitationSentAt,
        item.AcceptedScopeVersion, Etag.From(item.UpdatedAt));

    private static AppException NotAllowed(string reason) => new(AppErrorType.Conflict, ErrorCode.ExecutorNotAllowed,
        "Không thể thực hiện thao tác Executor lúc này.", new Dictionary<string, object?> { ["reason"] = reason });
}
