using LegacyVault.Application.Dtos;
using LegacyVault.Application.Exceptions;
using LegacyVault.Application.Notifications;
using LegacyVault.Application.Repositories;
using LegacyVault.Application.Security;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;

namespace LegacyVault.Application.Services;

public interface IExecutorWillService
{
    /// <summary>
    /// Người thực thi đồng ý (ký bằng mật khẩu) hoặc không đồng ý phiên bản Chủ kho ký lại. Không đồng ý thì vẫn giữ vai trò
    /// theo bản đã ký trước cho tới khi có người thay. Không phải Người thực thi của kho → 404; phiên bản cũ → 409
    /// EXECUTOR_NOT_ALLOWED reason WILL_VERSION_CHANGED; kế hoạch không chạy → reason PLAN_NOT_RUNNING.
    /// </summary>
    Task RespondAsync(Guid vaultId, RespondWillVersionRequest request, CancellationToken ct);
}

public sealed class ExecutorWillService : IExecutorWillService
{
    private readonly ICurrentUser currentUser;
    private readonly IVaultRepository vaults;
    private readonly IUserRepository users;
    private readonly IWillVersionRepository willVersions;
    private readonly ISigningPasswordChecker passwordChecker;
    private readonly IAuditLogRepository audit;
    private readonly IUnitOfWork unitOfWork;
    private readonly IExecutorWillNoticeSender notices;
    private readonly TimeProvider time;

    public ExecutorWillService(ICurrentUser currentUser, IVaultRepository vaults, IUserRepository users,
        IWillVersionRepository willVersions, ISigningPasswordChecker passwordChecker, IAuditLogRepository audit,
        IUnitOfWork unitOfWork, IExecutorWillNoticeSender notices, TimeProvider time)
    {
        this.currentUser = currentUser;
        this.vaults = vaults;
        this.users = users;
        this.willVersions = willVersions;
        this.passwordChecker = passwordChecker;
        this.audit = audit;
        this.unitOfWork = unitOfWork;
        this.notices = notices;
        this.time = time;
    }

    public async Task RespondAsync(Guid vaultId, RespondWillVersionRequest request, CancellationToken ct)
    {
        Guid userId = currentUser.RequireUserId();
        User? user = await users.FindByIdAsync(userId, ct);
        if (user == null || user.Status != UserStatus.Active)
        {
            throw new AppException(AppErrorType.Forbidden, ErrorCode.AccountNotActive, "Tài khoản hiện không được phép thực hiện thao tác.");
        }

        // 1. Kiểm quyền và phiên bản trước, rồi mới kiểm mật khẩu ngoài giao dịch để bộ đếm sai được giữ lại
        await LoadAsync(vaultId, userId, request, ct);
        if (request.Accept)
        {
            if (string.IsNullOrWhiteSpace(request.Password))
            {
                throw ValidationErrors.For("password", "Nhập mật khẩu tài khoản để ký biên bản phân chia.");
            }
            await passwordChecker.CheckAsync(user, request.Password, time.GetUtcNow(), ct);
        }

        // 2. Ghi câu trả lời; bấm lặp cùng câu trả lời thì giữ nguyên
        Guid? notifyOwnerId = null;
        await unitOfWork.ExecuteSerializableAsync(async () =>
        {
            notifyOwnerId = null;
            (Vault vault, Executor executor) = await LoadAsync(vaultId, userId, request, ct);
            DateTimeOffset now = time.GetUtcNow();
            if (request.Accept && executor.ConfirmedWillVersionNo != request.WillVersionNo)
            {
                executor.ConfirmedWillVersionNo = request.WillVersionNo;
                executor.WillConfirmedAt = now;
                executor.DeclinedWillVersionNo = null;
                executor.UpdatedAt = now;
                audit.Add(AuditRules.Create(vault.Id, userId, AuditActorRole.Executor, AuditAction.ExecutorWillConfirmed,
                    AuditTargetType.Vault, vault.Id, request.WillVersionNo, AuditOutcome.Succeeded, null, now));
            }
            if (!request.Accept && executor.DeclinedWillVersionNo != request.WillVersionNo)
            {
                if (executor.ConfirmedWillVersionNo == request.WillVersionNo)
                {
                    throw NotAllowed("WILL_ALREADY_CONFIRMED");
                }
                executor.DeclinedWillVersionNo = request.WillVersionNo;
                executor.UpdatedAt = now;
                audit.Add(AuditRules.Create(vault.Id, userId, AuditActorRole.Executor, AuditAction.ExecutorWillDeclined,
                    AuditTargetType.Vault, vault.Id, request.WillVersionNo, AuditOutcome.Succeeded, null, now));
                notifyOwnerId = vault.OwnerId;
            }
            await unitOfWork.SaveChangesAsync(ct);
            return true;
        }, ct);

        // 3. Không đồng ý thì báo Chủ kho sau khi đã lưu
        if (notifyOwnerId != null)
        {
            User? owner = await users.FindByIdAsync(notifyOwnerId.Value, ct);
            if (owner != null)
            {
                await notices.SendDeclinedAsync(owner.Email, user.FullName, request.WillVersionNo, ct);
            }
        }
    }

    private async Task<(Vault Vault, Executor Executor)> LoadAsync(
        Guid vaultId, Guid userId, RespondWillVersionRequest request, CancellationToken ct)
    {
        Vault? vault = await vaults.FindByIdAsync(vaultId, ct);
        Executor? executor = null;
        if (vault != null && vault.OwnerId != userId)
        {
            foreach (Executor item in vault.Executors)
            {
                if (item.UserId == userId && item.Status == ExecutorStatus.Accepted)
                {
                    executor = item;
                }
            }
        }
        if (vault == null || executor == null)
        {
            throw new AppException(AppErrorType.NotFound, ErrorCode.VaultNotFound, "Không tìm thấy kho.");
        }
        if (!PlanRules.IsRunning(vault.Plan.Status))
        {
            throw NotAllowed("PLAN_NOT_RUNNING");
        }
        WillVersion? latest = await willVersions.FindLatestByVaultIdAsync(vault.Id, ct);
        if (latest == null || latest.VersionNo != request.WillVersionNo)
        {
            throw NotAllowed("WILL_VERSION_CHANGED");
        }
        return (vault, executor);
    }

    private static AppException NotAllowed(string reason)
    {
        return new AppException(AppErrorType.Conflict, ErrorCode.ExecutorNotAllowed,
            "Không thể trả lời biên bản phân chia lúc này. Tải lại để xem bản mới nhất.",
            new Dictionary<string, object?> { ["reason"] = reason });
    }
}
