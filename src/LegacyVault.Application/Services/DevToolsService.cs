using LegacyVault.Application.Dtos.Requests;
using LegacyVault.Application.Dtos.Responses;
using LegacyVault.Application.Ekyc;
using LegacyVault.Application.Exceptions;
using LegacyVault.Application.Mappings;
using LegacyVault.Application.Repositories;
using LegacyVault.Application.Security;
using LegacyVault.Application.Storage;
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
    IEntitlementExpiryJob entitlementExpiryJob,
    IOperationsRecorder operationsRecorder,
    TimeProvider timeProvider,
    IEkycOptions ekycOptions,
    IAssetContentStore contentStore) : IDevToolsService
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
        bool runEntitlementExpiry = false;

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
            // Lùi lần điểm danh gần nhất theo hạn mới để luật "một lần mỗi ngày" không chặn Owner khi test quá hạn.
            CheckInSchedule? schedule = await repository.FindCheckInScheduleAsync(vault.Plan.Id, ct);
            if (schedule != null)
            {
                DateTimeOffset? shiftedLastCheckIn = DevToolsRules.LastCheckInAfterShiftedDue(
                    schedule.LastCheckInAt, period.DueAt, schedule.CycleDays);
                if (shiftedLastCheckIn != schedule.LastCheckInAt)
                {
                    schedule.LastCheckInAt = shiftedLastCheckIn;
                    schedule.UpdatedAt = now;
                }
            }
            targetType = AuditTargetType.CheckIn;
            targetId = period.Id;
            periodToProcess = period.Id;
        }
        else if (key == DevToolsRules.ServiceExpired)
        {
            // OPLAN-01: dời hạn dịch vụ về quá khứ; job hết hạn quyết định chuyển sang ân hạn như khi chạy nền.
            VaultEntitlement entitlement = vault.Entitlement;
            EnsureAvailable(DevToolsRules.EvaluateServiceExpired(entitlement));
            if (entitlement.ServiceExpiresAt!.Value > past)
            {
                entitlement.ServiceExpiresAt = past;
            }
            entitlement.UpdatedAt = now;
            targetType = AuditTargetType.Vault;
            targetId = vault.Id;
            runEntitlementExpiry = true;
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
        if (runEntitlementExpiry)
        {
            // Chạy ngay job hết hạn cho kho này, giống job nền, để màn hình thấy kho đã sang ân hạn.
            await entitlementExpiryJob.ProcessVaultAsync(vault.Id, ct);
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

        // Xóa eKYC và cả kho trong một transaction: lỗi giữa chừng thì không để lại kho nửa còn nửa mất.
        List<string> storageKeys = new List<string>();
        DevEkycResetResponse response = await unitOfWork.ExecuteSerializableAsync(async () =>
        {
            DevEkycResetResponse result = await RemoveEkycDataAsync(target, storageKeys, ct);
            Guid? vaultId = await repository.FindVaultIdByOwnerAsync(target.Id, ct);
            if (vaultId != null)
            {
                List<string> vaultKeys = await repository.ListVaultStorageKeysAsync(vaultId.Value, target.Id, ct);
                AddStorageKeys(storageKeys, vaultKeys);
                await repository.DeleteVaultAsync(vaultId.Value, target.Id, ct);
                result.VaultRemoved = true;
            }
            if (!HasRemovedEkycData(result) && !result.VaultRemoved)
            {
                return result;
            }

            // Nhật ký không gắn kho (kho vừa bị xóa) và không ghi số CCCD; chỉ ghi tài khoản bị đặt lại.
            auditLogs.Add(AuditRules.Create(null, adminId, AuditActorRole.Admin, AuditAction.DevEkycReset,
                AuditTargetType.User, target.Id, null, AuditOutcome.Succeeded, null, timeProvider.GetUtcNow()));
            await unitOfWork.SaveChangesAsync(ct);
            return result;
        }, ct);

        // Chỉ xóa tệp sau khi DB đã lưu xong, để lỗi DB không làm mất tệp của dữ liệu còn giữ.
        response.FilesRemoved = await DeleteStoredFilesAsync(storageKeys);
        return response;
    }

    /// <summary>
    /// Xóa từng tệp mã hóa khỏi kho lưu trữ. Một tệp lỗi không chặn các tệp còn lại: bản ghi DB đã xóa nên tệp sót
    /// chỉ chiếm chỗ, không ai đọc được nữa. Trả số tệp đã xóa (kể cả tệp vốn không còn trên đĩa).
    /// </summary>
    private async Task<int> DeleteStoredFilesAsync(List<string> storageKeys)
    {
        int removed = 0;
        foreach (string storageKey in storageKeys)
        {
            try
            {
                await contentStore.DeleteAsync(storageKey, CancellationToken.None);
                removed++;
            }
            catch (Exception)
            {
                // Bỏ qua tệp lỗi; DB đã nhất quán.
            }
        }
        return removed;
    }

    private static void AddStorageKeys(List<string> storageKeys, List<string> candidates)
    {
        foreach (string candidate in candidates)
        {
            if (!storageKeys.Contains(candidate))
            {
                storageKeys.Add(candidate);
            }
        }
    }

    public async Task<DevEkycThresholdResponse> GetEkycThresholdAsync(CancellationToken ct)
    {
        await RequireAdminAsync(ct);
        return BuildThresholdResponse();
    }

    public async Task<DevEkycThresholdResponse> UpdateEkycThresholdAsync(
        DevEkycThresholdRequest request, CancellationToken ct)
    {
        Guid adminId = await RequireAdminAsync(ct);
        double threshold = request.Threshold;
        if (!DevToolsRules.IsSafeEkycThreshold(threshold))
        {
            throw ValidationErrors.For(
                "threshold",
                $"Ngưỡng so khớp phải nằm trong dải an toàn từ {DevToolsRules.EkycThresholdMin:0.00} đến {DevToolsRules.EkycThresholdMax:0.00}.");
        }

        // Override runtime trong bộ nhớ: EkycService/OwnerFaceVerifier đọc CurrentThreshold sau thay đổi;
        // không ghi DB nên khởi động lại là về DefaultThreshold.
        double previous = ekycOptions.CurrentThreshold;
        ekycOptions.SetThreshold(threshold);

        // Sự kiện toàn cục (không gắn kho): đối tượng là Configuration, ghi rõ giá trị cũ → mới.
        string reason = $"threshold {previous:0.00} -> {threshold:0.00}";
        auditLogs.Add(AuditRules.Create(null, adminId, AuditActorRole.Admin, AuditAction.DevUpdateEkycThreshold,
            AuditTargetType.Configuration, null, null, AuditOutcome.Succeeded, reason, timeProvider.GetUtcNow()));
        await unitOfWork.SaveChangesAsync(ct);
        return BuildThresholdResponse();
    }

    private DevEkycThresholdResponse BuildThresholdResponse()
    {
        return new DevEkycThresholdResponse
        {
            CurrentThreshold = ekycOptions.CurrentThreshold,
            DefaultThreshold = ekycOptions.DefaultThreshold,
            Min = DevToolsRules.EkycThresholdMin,
            Max = DevToolsRules.EkycThresholdMax,
        };
    }

    public async Task<DevRewindBeforeEkycResponse> RewindBeforeEkycAsync(Guid vaultId, CancellationToken ct)
    {
        Guid adminId = await RequireAdminAsync(ct);
        Vault? vault = await repository.FindVaultWithEntitlementAsync(vaultId, ct);
        if (vault == null)
        {
            throw new AppException(AppErrorType.NotFound, ErrorCode.VaultNotFound, "Không tìm thấy kho.");
        }
        User? owner = await users.FindByIdAsync(vault.OwnerId, ct);
        if (owner == null)
        {
            throw new AppException(AppErrorType.NotFound, ErrorCode.UserNotFound, "Không tìm thấy chủ kho.");
        }
        if (vault.Plan.Status != PlanStatus.Draft)
        {
            // Kế hoạch đã chạy dựa vào eKYC và gói đã trả; rút lại giữa chừng làm dữ liệu kế hoạch mâu thuẫn.
            Dictionary<string, object?> extensions = new Dictionary<string, object?>();
            extensions["reason"] = "PLAN_NOT_DRAFT";
            throw new AppException(AppErrorType.Conflict, ErrorCode.DevToolNotAllowed,
                "Kế hoạch đã kích hoạt nên không thể đưa kho về trước eKYC và mua gói.", extensions);
        }

        DateTimeOffset now = timeProvider.GetUtcNow();
        DevEkycResetResponse ekyc = await RemoveEkycDataAsync(owner, new List<string>(), ct);

        DevRewindBeforeEkycResponse response = new DevRewindBeforeEkycResponse();
        response.VaultId = vault.Id;
        response.OwnerEmail = owner.Email;
        response.VerificationsRemoved = ekyc.VerificationsRemoved;
        response.ConsentsRemoved = ekyc.ConsentsRemoved;
        response.IdentityRemoved = ekyc.IdentityRemoved;
        response.FaceTemplatesRemoved = ekyc.FaceTemplatesRemoved;
        response.PreviousTier = vault.Entitlement.Tier.ToApiString();

        // Trả quyền dịch vụ về đúng giá trị của kho mới tạo (VaultService).
        VaultEntitlement entitlement = vault.Entitlement;
        bool alreadyFree = entitlement.Tier == ServiceTier.Free
            && entitlement.Status == EntitlementStatus.Free
            && entitlement.StorageQuotaBytes == StorageLimits.FreeQuotaBytes
            && entitlement.IncludedBeneficiarySlots == 0
            && entitlement.ExtraBeneficiarySlots == 0
            && entitlement.ServiceExpiresAt == null;
        if (!alreadyFree)
        {
            entitlement.Tier = ServiceTier.Free;
            entitlement.Status = EntitlementStatus.Free;
            entitlement.StorageQuotaBytes = StorageLimits.FreeQuotaBytes;
            entitlement.IncludedBeneficiarySlots = 0;
            entitlement.ExtraBeneficiarySlots = 0;
            entitlement.ServiceExpiresAt = null;
            entitlement.UpdatedAt = now;
            response.EntitlementReset = true;
        }

        // Đơn đã trả giữ nguyên làm lịch sử; chỉ hủy đơn đang chờ để tiền về sau không cấp lại quyền ngoài ý muốn.
        List<PaymentOrder> pendingOrders = await repository.ListPendingPaymentOrdersAsync(vault.Id, ct);
        foreach (PaymentOrder order in pendingOrders)
        {
            order.Status = PaymentOrderStatus.Cancelled;
            order.CancelledAt = now;
            order.UpdatedAt = now;
        }
        response.PendingOrdersCancelled = pendingOrders.Count;

        bool changedAnything = HasRemovedEkycData(ekyc) || response.EntitlementReset || pendingOrders.Count > 0;
        if (!changedAnything)
        {
            return response;
        }

        auditLogs.Add(AuditRules.Create(vault.Id, adminId, AuditActorRole.Admin, AuditAction.DevRewindBeforeEkyc,
            AuditTargetType.Vault, vault.Id, null, AuditOutcome.Succeeded, null, now));
        await unitOfWork.SaveChangesAsync(ct);
        return response;
    }

    /// <summary>Đánh dấu xóa mọi dữ liệu eKYC của tài khoản (chưa lưu) và trả số lượng đã đánh dấu.</summary>
    /// <summary>Đánh dấu xóa eKYC của tài khoản; thêm khóa ảnh CCCD đã mã hóa vào storageKeys để xóa tệp sau khi lưu.</summary>
    private async Task<DevEkycResetResponse> RemoveEkycDataAsync(User target, List<string> storageKeys, CancellationToken ct)
    {
        List<EkycVerification> verifications = await repository.ListEkycVerificationsAsync(target.Id, ct);
        List<EkycConsent> consents = await repository.ListEkycConsentsAsync(target.Id, ct);
        OwnerIdentity? identity = await repository.FindOwnerIdentityAsync(target.Id, ct);
        List<OwnerFaceTemplate> faceTemplates = await repository.ListOwnerFaceTemplatesAsync(target.Id, ct);

        List<string> imageKeys = new List<string>();
        foreach (EkycVerification verification in verifications)
        {
            if (!string.IsNullOrWhiteSpace(verification.PendingDocumentImageStorageKey))
            {
                imageKeys.Add(verification.PendingDocumentImageStorageKey);
            }
        }
        if (identity != null && !string.IsNullOrWhiteSpace(identity.DocumentImageStorageKey))
        {
            imageKeys.Add(identity.DocumentImageStorageKey);
        }
        AddStorageKeys(storageKeys, imageKeys);

        DevEkycResetResponse response = new DevEkycResetResponse();
        response.UserId = target.Id;
        response.Email = target.Email;
        response.VerificationsRemoved = verifications.Count;
        response.ConsentsRemoved = consents.Count;
        response.IdentityRemoved = identity != null;
        response.FaceTemplatesRemoved = faceTemplates.Count;

        if (HasRemovedEkycData(response))
        {
            repository.RemoveEkycData(verifications, consents, identity, faceTemplates);
        }
        return response;
    }

    private static bool HasRemovedEkycData(DevEkycResetResponse response)
    {
        if (response.VerificationsRemoved > 0 || response.ConsentsRemoved > 0
            || response.IdentityRemoved || response.FaceTemplatesRemoved > 0)
        {
            return true;
        }
        return false;
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
        response.Milestones.Add(DevToolsRules.EvaluateServiceExpired(vault.Entitlement));
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
        // Kèm quyền dịch vụ để đánh giá và tua mốc hết hạn dịch vụ.
        Vault? vault = await repository.FindVaultWithEntitlementAsync(vaultId, ct);
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
