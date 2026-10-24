using LegacyVault.Application.Dtos.Requests;
using LegacyVault.Application.Dtos.Responses;
using LegacyVault.Application.Exceptions;
using LegacyVault.Application.Mappings;
using LegacyVault.Application.Repositories;
using LegacyVault.Application.Security;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;

namespace LegacyVault.Application.Services;

public sealed class DevToolsService(
    ICurrentUser currentUser,
    IUserRepository users,
    IDevToolsRepository repository,
    IAuditLogRepository auditLogs,
    IUnitOfWork unitOfWork,
    ICheckInJob checkInJob,
    IOperationsRecorder operationsRecorder,
    TimeProvider timeProvider) : IDevToolsService
{
    /// <summary>Đủ cho dữ liệu thử trên máy dev; ô chọn không cần phân trang.</summary>
    private const int MaxVaults = 200;

    public async Task<List<DevVaultOptionResponse>> ListVaultsAsync(CancellationToken ct)
    {
        await RequireAdminAsync(ct);
        List<Vault> vaults = await repository.ListVaultsAsync(MaxVaults, ct);
        List<DevVaultOptionResponse> items = new List<DevVaultOptionResponse>();
        foreach (Vault vault in vaults)
        {
            DevVaultOptionResponse item = new DevVaultOptionResponse();
            item.VaultId = vault.Id;
            item.VaultName = vault.Name;
            item.OwnerName = vault.Owner.FullName;
            item.PlanStatus = vault.Plan.Status.ToApiString();
            items.Add(item);
        }
        return items;
    }

    public async Task<DevClockResponse> GetClockAsync(Guid vaultId, CancellationToken ct)
    {
        await RequireAdminAsync(ct);
        Vault vault = await LoadVaultAsync(vaultId, ct);
        return await BuildClockAsync(vault, ct);
    }

    public async Task<DevClockResponse> TimeTravelAsync(Guid vaultId, DevTimeTravelRequest request, CancellationToken ct)
    {
        Guid adminId = await RequireAdminAsync(ct);
        string key = request.Milestone.Trim().ToUpperInvariant();
        if (!DevToolsRules.IsKnownMilestone(key))
        {
            throw ValidationErrors.For("milestone", "Mốc cần tua không hợp lệ.");
        }

        Vault vault = await LoadVaultAsync(vaultId, ct);
        DateTimeOffset now = timeProvider.GetUtcNow();
        DateTimeOffset past = now - DevToolsRules.PastMargin;
        AuditTargetType targetType;
        Guid targetId;
        Guid? periodToProcess = null;

        if (key == DevToolsRules.CheckInOverdue || key == DevToolsRules.GraceEnded)
        {
            CheckInPeriod? period = await repository.FindOpenPeriodAsync(vault.Plan.Id, ct);
            DevMilestoneResponse state;
            if (key == DevToolsRules.CheckInOverdue)
            {
                state = DevToolsRules.EvaluateCheckInOverdue(vault.Plan.Status, period, now);
            }
            else
            {
                state = DevToolsRules.EvaluateGraceEnded(vault.Plan.Status, period, now);
            }
            EnsureAvailable(state);

            // Mốc 1 chỉ qua hạn điểm danh, thời gian chờ vẫn còn. Mốc 2 cho qua luôn thời gian chờ.
            if (key == DevToolsRules.CheckInOverdue)
            {
                period!.DueAt = past;
            }
            else
            {
                period!.GraceEndsAt = past;
                if (period.DueAt > period.GraceEndsAt)
                {
                    period.DueAt = period.GraceEndsAt - DevToolsRules.PastMargin;
                }
            }
            targetType = AuditTargetType.CheckIn;
            targetId = period.Id;
            periodToProcess = period.Id;
        }
        else if (key == DevToolsRules.ObjectionWindowEnded)
        {
            DeathCase? openCase = await repository.FindOpenDeathCaseAsync(vault.Id, ct);
            EnsureAvailable(DevToolsRules.EvaluateObjectionWindow(openCase, now));
            openCase!.ObjectionEndsAt = past;
            openCase.ObjectionStartsAt = past.AddHours(-DeathCaseRules.ObjectionHours);
            openCase.UpdatedAt = now;
            targetType = AuditTargetType.DeathCase;
            targetId = openCase.Id;
        }
        else
        {
            DeathCaseObjection? objection = await repository.FindLatestObjectionAsync(vault.Id, ct);
            EnsureAvailable(DevToolsRules.EvaluateSelfieDeadline(objection, now));
            objection!.SelfieDeadlineAt = past;
            objection.UpdatedAt = now;
            targetType = AuditTargetType.DeathCase;
            targetId = objection.CaseId;
        }

        auditLogs.Add(AuditRules.Create(vault.Id, adminId, AuditActorRole.Admin, AuditAction.DevTimeTravel,
            targetType, targetId, null, AuditOutcome.Succeeded, key, now));
        await unitOfWork.SaveChangesAsync(ct);

        if (periodToProcess != null)
        {
            // Chạy ngay vòng xử lý của job điểm danh cho kỳ này, giống job nền, để màn hình thấy trạng thái mới liền.
            await checkInJob.ProcessPeriodAsync(periodToProcess.Value, ct);
            await operationsRecorder.RecordCheckInPeriodAsync(periodToProcess.Value, ct);
        }

        Vault refreshed = await LoadVaultAsync(vault.Id, ct);
        return await BuildClockAsync(refreshed, ct);
    }

    public async Task<DevClockResponse> ResetClockAsync(Guid vaultId, CancellationToken ct)
    {
        Guid adminId = await RequireAdminAsync(ct);
        Vault vault = await LoadVaultAsync(vaultId, ct);
        AuditAction? latest = await repository.FindLatestClockActionAsync(vault.Id, ct);
        if (latest == AuditAction.DevTimeTravel)
        {
            DateTimeOffset now = timeProvider.GetUtcNow();
            auditLogs.Add(AuditRules.Create(vault.Id, adminId, AuditActorRole.Admin, AuditAction.DevClockReset,
                AuditTargetType.Vault, vault.Id, null, AuditOutcome.Succeeded, null, now));
            await unitOfWork.SaveChangesAsync(ct);
        }
        return await BuildClockAsync(vault, ct);
    }

    public async Task<DevEkycResetResponse> ResetEkycAsync(DevEkycResetRequest request, CancellationToken ct)
    {
        Guid adminId = await RequireAdminAsync(ct);
        string email = request.Email.Trim().ToLowerInvariant();
        User? target = await users.FindByEmailAsync(email, ct);
        if (target == null)
        {
            throw new AppException(AppErrorType.NotFound, ErrorCode.UserNotFound,
                "Không tìm thấy tài khoản với email này.");
        }

        List<EkycVerification> verifications = await repository.ListEkycVerificationsAsync(target.Id, ct);
        List<EkycConsent> consents = await repository.ListEkycConsentsAsync(target.Id, ct);
        OwnerIdentity? identity = await repository.FindOwnerIdentityAsync(target.Id, ct);
        List<OwnerFaceTemplate> faceTemplates = await repository.ListOwnerFaceTemplatesAsync(target.Id, ct);

        DevEkycResetResponse response = new DevEkycResetResponse();
        response.UserId = target.Id;
        response.Email = target.Email;
        response.VerificationsRemoved = verifications.Count;
        response.ConsentsRemoved = consents.Count;
        response.IdentityRemoved = identity != null;
        response.FaceTemplatesRemoved = faceTemplates.Count;

        bool nothingToRemove = verifications.Count == 0 && consents.Count == 0
            && identity == null && faceTemplates.Count == 0;
        if (nothingToRemove)
        {
            return response;
        }

        repository.RemoveEkycData(verifications, consents, identity, faceTemplates);
        // Nhật ký không gắn kho và không ghi số CCCD; chỉ ghi tài khoản bị đặt lại.
        auditLogs.Add(AuditRules.Create(null, adminId, AuditActorRole.Admin, AuditAction.DevEkycReset,
            AuditTargetType.User, target.Id, null, AuditOutcome.Succeeded, null, timeProvider.GetUtcNow()));
        await unitOfWork.SaveChangesAsync(ct);
        return response;
    }

    private async Task<DevClockResponse> BuildClockAsync(Vault vault, CancellationToken ct)
    {
        DateTimeOffset now = timeProvider.GetUtcNow();
        PlanStatus status = vault.Plan.Status;
        CheckInPeriod? period = await repository.FindOpenPeriodAsync(vault.Plan.Id, ct);
        DeathCase? openCase = await repository.FindOpenDeathCaseAsync(vault.Id, ct);
        DeathCaseObjection? objection = await repository.FindLatestObjectionAsync(vault.Id, ct);
        AuditAction? latestClockAction = await repository.FindLatestClockActionAsync(vault.Id, ct);

        DevClockResponse response = new DevClockResponse();
        response.VaultId = vault.Id;
        response.PlanStatus = status.ToApiString();
        response.CheckInState = DevToolsRules.GetCheckInState(status, period, now);
        response.ClockState = DevToolsRules.GetClockState(latestClockAction);
        response.Milestones.Add(DevToolsRules.EvaluateCheckInOverdue(status, period, now));
        response.Milestones.Add(DevToolsRules.EvaluateGraceEnded(status, period, now));
        response.Milestones.Add(DevToolsRules.EvaluateObjectionWindow(openCase, now));
        response.Milestones.Add(DevToolsRules.EvaluateSelfieDeadline(objection, now));
        return response;
    }

    private static void EnsureAvailable(DevMilestoneResponse state)
    {
        if (state.State == DevToolsRules.Available)
        {
            return;
        }
        string message = state.Note;
        if (state.State == DevToolsRules.Passed)
        {
            message = "Kho đã qua mốc này.";
        }
        Dictionary<string, object?> extensions = new Dictionary<string, object?>();
        extensions["milestone"] = state.Key;
        extensions["state"] = state.State;
        throw new AppException(AppErrorType.Conflict, ErrorCode.DevToolNotAllowed, message, extensions);
    }

    private async Task<Vault> LoadVaultAsync(Guid vaultId, CancellationToken ct)
    {
        Vault? vault = await repository.FindVaultAsync(vaultId, ct);
        if (vault == null)
        {
            throw new AppException(AppErrorType.NotFound, ErrorCode.VaultNotFound, "Không tìm thấy kho.");
        }
        return vault;
    }

    /// <summary>Quyền Admin đọc từ DB mỗi lần gọi; không phải Admin đang hoạt động thì 404 như các API quản trị khác.</summary>
    private async Task<Guid> RequireAdminAsync(CancellationToken ct)
    {
        Guid userId = currentUser.RequireUserId();
        User? user = await users.FindByIdAsync(userId, ct);
        if (user == null || user.Role != UserRole.Admin || user.Status != UserStatus.Active)
        {
            throw new AppException(AppErrorType.NotFound, ErrorCode.UserNotFound, "Không tìm thấy tài khoản.");
        }
        return userId;
    }
}
