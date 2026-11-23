using LegacyVault.Application.Dtos.Requests;
using LegacyVault.Application.Dtos.Responses;
using LegacyVault.Application.Exceptions;
using LegacyVault.Application.Mappings;
using LegacyVault.Application.Repositories;
using LegacyVault.Application.Security;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;

namespace LegacyVault.Application.Services;

public sealed class AssetAssignmentService(
    ICurrentUser currentUser,
    IVaultRepository vaultRepository,
    IAssetRepository assetRepository,
    IBeneficiaryRepository beneficiaryRepository,
    IAssetAssignmentRepository assignmentRepository,
    IAuditLogRepository auditLogRepository,
    TimeProvider timeProvider) : IAssetAssignmentService
{
    public async Task<List<AssetAssignmentResponse>> GetAssignmentsAsync(Guid vaultId, CancellationToken ct)
    {
        // Xem không cần kho đang cho sửa, chỉ cần kho thuộc người dùng.
        Vault vault = await LoadOwnedVaultAsync(vaultId, ct);
        List<AssetAssignment> assignments = await assignmentRepository.FindByVaultIdAsync(vault.Id, ct);
        return assignments.ToResponses();
    }

    public async Task<List<AssetAssignmentResponse>> SetAssetRecipientsAsync(
        Guid vaultId, Guid assetId, SetAssetRecipientsRequest request, CancellationToken ct)
    {
        Vault vault = await LoadOwnedVaultAsync(vaultId, ct);
        Asset asset = await LoadAssetAsync(vault.Id, assetId, ct);

        // Bỏ id trùng trong danh sách gửi lên; mỗi người chỉ được gán một lần cho một tài sản.
        List<Guid> finalIds = new List<Guid>();
        foreach (Guid beneficiaryId in request.BeneficiaryIds)
        {
            if (!finalIds.Contains(beneficiaryId))
            {
                finalIds.Add(beneficiaryId);
            }
        }

        // Người nhận phải thuộc đúng kho này; hồ sơ kho khác trả 404 như không tồn tại.
        foreach (Guid beneficiaryId in finalIds)
        {
            Beneficiary? beneficiary = await beneficiaryRepository.FindByIdAsync(vault.Id, beneficiaryId, ct);
            if (beneficiary == null)
            {
                throw new AppException(AppErrorType.NotFound, ErrorCode.BeneficiaryNotFound, "Không tìm thấy người nhận.");
            }
        }

        List<AssetAssignment> vaultAssignments = await assignmentRepository.FindByVaultIdAsync(vault.Id, ct);
        return await ApplyAsync(vault, asset, vaultAssignments, finalIds, request.ConfirmPlanDeactivation, ct);
    }

    public async Task<List<AssetAssignmentResponse>> RemoveAssignmentAsync(
        Guid vaultId, Guid assetId, Guid beneficiaryId, ConfirmPlanImpactRequest? request, CancellationToken ct)
    {
        Vault vault = await LoadOwnedVaultAsync(vaultId, ct);
        Asset asset = await LoadAssetAsync(vault.Id, assetId, ct);

        bool confirmed = false;
        if (request != null)
        {
            confirmed = request.ConfirmPlanDeactivation;
        }

        // Danh sách người nhận sau cùng = người nhận hiện tại của tài sản, trừ người cần gỡ.
        List<AssetAssignment> vaultAssignments = await assignmentRepository.FindByVaultIdAsync(vault.Id, ct);
        List<Guid> finalIds = new List<Guid>();
        foreach (AssetAssignment assignment in vaultAssignments)
        {
            if (assignment.AssetId == asset.Id && assignment.BeneficiaryId != beneficiaryId)
            {
                finalIds.Add(assignment.BeneficiaryId);
            }
        }
        return await ApplyAsync(vault, asset, vaultAssignments, finalIds, confirmed, ct);
    }

    /// <summary>
    /// Đưa danh sách người nhận của tài sản về đúng finalIds: thêm người mới, gỡ người không còn trong danh sách.
    /// vaultAssignments là mọi dòng gán của kho đang được theo dõi. Không có gì đổi thì không ghi và không tăng phiên bản cấu hình.
    /// </summary>
    private async Task<List<AssetAssignmentResponse>> ApplyAsync(
        Vault vault, Asset asset, List<AssetAssignment> vaultAssignments, List<Guid> finalIds,
        bool confirmed, CancellationToken ct)
    {
        List<AssetAssignment> current = new List<AssetAssignment>();
        foreach (AssetAssignment assignment in vaultAssignments)
        {
            if (assignment.AssetId == asset.Id)
            {
                current.Add(assignment);
            }
        }

        List<Guid> toAdd = new List<Guid>();
        foreach (Guid beneficiaryId in finalIds)
        {
            if (FindByBeneficiary(current, beneficiaryId) == null)
            {
                toAdd.Add(beneficiaryId);
            }
        }
        List<AssetAssignment> toRemove = new List<AssetAssignment>();
        foreach (AssetAssignment assignment in current)
        {
            if (!finalIds.Contains(assignment.BeneficiaryId))
            {
                toRemove.Add(assignment);
            }
        }

        // Không có gì đổi: trả nguyên trạng, không lưu, không tăng phiên bản cấu hình.
        if (toAdd.Count == 0 && toRemove.Count == 0)
        {
            return current.ToResponses();
        }

        if (toAdd.Count > 0)
        {
            // Thêm người nhận là thao tác "thêm": chặn khi kho đóng, kế hoạch bị khóa hoặc dịch vụ đang ân hạn.
            VaultRules.EnsureEditable(vault);
            // Hạng Free chưa được gán tài sản; chặn ở API, không chỉ ẩn nút.
            if (vault.Entitlement.Tier == ServiceTier.Free)
            {
                throw new AppException(AppErrorType.Forbidden, ErrorCode.PaidTierRequired,
                    "Hạng miễn phí chưa được gán tài sản cho người nhận. Vui lòng nâng cấp dịch vụ.");
            }
            EnsureSlotsEnough(vault, vaultAssignments, toRemove, toAdd);
        }
        else
        {
            // Chỉ gỡ là thao tác "giảm": vẫn cho khi dịch vụ đang ân hạn.
            VaultRules.EnsureCanReduce(vault);
        }

        DateTimeOffset now = timeProvider.GetUtcNow();

        // Kế hoạch đang chạy mà thao tác làm kho không còn dòng gán nào thì Chủ kho phải xác nhận.
        int remaining = vaultAssignments.Count - toRemove.Count + toAdd.Count;
        if (toRemove.Count > 0 && remaining == 0 && PlanRules.IsRunning(vault.Plan.Status))
        {
            if (!confirmed)
            {
                throw new AppException(AppErrorType.Conflict, ErrorCode.PlanDeactivationConfirmationRequired,
                    "Thao tác này làm kế hoạch không còn tài sản nào được gán cho người nhận và kế hoạch sẽ trở về bản nháp. Vui lòng xác nhận.");
            }
            PlanRules.Deactivate(vault, now);
            auditLogRepository.Add(AuditRules.OwnerAction(
                vault.Id, vault.OwnerId, AuditAction.PlanDeactivated, AuditTargetType.Plan, vault.Plan.Id,
                vault.Plan.ConfigVersion, now));
        }

        // Mọi thay đổi gán tăng phiên bản cấu hình đúng một lần cho cả thao tác.
        PlanRules.BumpConfigVersion(vault.Plan, now);

        foreach (AssetAssignment assignment in toRemove)
        {
            assignmentRepository.Remove(assignment);
            current.Remove(assignment);
            AddAudit(vault, AuditAction.AssetUnassigned, asset.Id, assignment.BeneficiaryId, now);
        }
        foreach (Guid beneficiaryId in toAdd)
        {
            AssetAssignment added = new AssetAssignment();
            added.AssetId = asset.Id;
            added.BeneficiaryId = beneficiaryId;
            added.CreatedAt = now;
            assignmentRepository.Add(added);
            current.Add(added);
            AddAudit(vault, AuditAction.AssetAssigned, asset.Id, beneficiaryId, now);
        }

        // Hai yêu cầu cùng gán một cặp: chỉ mục duy nhất chặn bên sau, báo tải lại thay vì ghi một nửa.
        // Hai yêu cầu đổi kho cùng lúc vấp RowVersion của kế hoạch và GlobalExceptionHandler trả 409 EDIT_CONFLICT.
        bool saved = await assignmentRepository.TrySaveChangesAsync(ct);
        if (!saved)
        {
            throw new AppException(AppErrorType.Conflict, ErrorCode.EditConflict,
                "Dữ liệu vừa thay đổi. Vui lòng tải lại.");
        }
        return current.ToResponses();
    }

    /// <summary>
    /// Suất người nhận đếm theo số NGƯỜI khác nhau có dòng gán trong kho. Người đã được gán tài sản khác không tốn thêm suất;
    /// người mới thì phải còn suất. Đã trừ những người mất dòng gán cuối cùng trong cùng thao tác.
    /// </summary>
    private static void EnsureSlotsEnough(
        Vault vault, List<AssetAssignment> vaultAssignments, List<AssetAssignment> toRemove, List<Guid> toAdd)
    {
        HashSet<Guid> before = new HashSet<Guid>();
        foreach (AssetAssignment assignment in vaultAssignments)
        {
            before.Add(assignment.BeneficiaryId);
        }

        HashSet<Guid> removedIds = new HashSet<Guid>();
        foreach (AssetAssignment assignment in toRemove)
        {
            removedIds.Add(assignment.Id);
        }
        HashSet<Guid> after = new HashSet<Guid>();
        foreach (AssetAssignment assignment in vaultAssignments)
        {
            if (!removedIds.Contains(assignment.Id))
            {
                after.Add(assignment.BeneficiaryId);
            }
        }
        foreach (Guid beneficiaryId in toAdd)
        {
            after.Add(beneficiaryId);
        }

        int maxSlots = EntitlementLimits.EffectiveTotalSlots(vault.Entitlement);
        if (after.Count > before.Count && after.Count > maxSlots)
        {
            Dictionary<string, object?> extensions = new Dictionary<string, object?>();
            extensions["usedSlots"] = before.Count;
            extensions["maxSlots"] = maxSlots;
            throw new AppException(AppErrorType.Conflict, ErrorCode.BeneficiarySlotsExceeded,
                "Kho đã dùng hết suất người nhận. Vui lòng mua thêm suất.", extensions);
        }
    }

    /// <summary>Một dòng nhật ký cho thao tác gán, lưu cùng lần lưu của thao tác. Chỉ ghi id người nhận, không ghi tên hay số CCCD.</summary>
    private void AddAudit(Vault vault, AuditAction action, Guid assetId, Guid beneficiaryId, DateTimeOffset now)
    {
        auditLogRepository.Add(AuditRules.Create(
            vault.Id, vault.OwnerId, AuditActorRole.Owner, action, AuditTargetType.Asset, assetId,
            vault.Plan.ConfigVersion, AuditOutcome.Succeeded, "Người nhận " + beneficiaryId, now));
    }

    private async Task<Vault> LoadOwnedVaultAsync(Guid vaultId, CancellationToken ct)
    {
        Guid userId = currentUser.RequireUserId();
        Vault? vault = await vaultRepository.FindByIdAsync(vaultId, ct);
        return VaultRules.EnsureOwnedBy(vault, userId);
    }

    /// <summary>Tài sản không có hoặc thuộc kho khác → 404 ASSET_NOT_FOUND.</summary>
    private async Task<Asset> LoadAssetAsync(Guid vaultId, Guid assetId, CancellationToken ct)
    {
        Asset? asset = await assetRepository.FindByIdAsync(vaultId, assetId, ct);
        if (asset == null)
        {
            throw new AppException(AppErrorType.NotFound, ErrorCode.AssetNotFound, "Không tìm thấy tài sản.");
        }
        return asset;
    }

    private static AssetAssignment? FindByBeneficiary(List<AssetAssignment> assignments, Guid beneficiaryId)
    {
        foreach (AssetAssignment assignment in assignments)
        {
            if (assignment.BeneficiaryId == beneficiaryId)
            {
                return assignment;
            }
        }
        return null;
    }
}
