using LegacyVault.Application.Exceptions;
using LegacyVault.Application.Mappings;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;

namespace LegacyVault.Application.Services;

/// <summary>
/// Luật kiểm tra kho dùng chung cho các service thao tác trên kho (Vault, Asset, Container...).
/// </summary>
public static class VaultRules
{
    /// <summary>
    /// Kho phải tồn tại và thuộc người đang đăng nhập; không thì 404 VAULT_NOT_FOUND
    /// (kho người khác cũng 404 để không lộ sự tồn tại, SEC-04).
    /// </summary>
    public static Vault EnsureOwnedBy(Vault? vault, Guid userId)
    {
        if (vault is null || vault.OwnerId != userId)
        {
            throw new AppException(AppErrorType.NotFound, ErrorCode.VaultNotFound, "Không tìm thấy kho.");
        }
        return vault;
    }

    /// <summary>
    /// Thao tác "thêm" (tạo/sửa tài sản, tạo/đổi tên gói...) chỉ được khi kho mở, kế hoạch chưa bị khóa
    /// và dịch vụ không ở chế độ chỉ đọc (OPLAN-01). Vi phạm → 409 VAULT_NOT_EDITABLE + reason.
    /// Cần vault đã nạp Plan và Entitlement.
    /// </summary>
    public static void EnsureEditable(Vault vault)
    {
        var reason = GetNotEditableReason(vault);
        if (reason is not null)
        {
            throw new AppException(AppErrorType.Conflict, ErrorCode.VaultNotEditable,
                "Kho hiện không cho phép chỉnh sửa.",
                new Dictionary<string, object?> { ["reason"] = reason.Value.ToApiString() });
        }
    }

    /// <summary>
    /// Thao tác "giảm" (xóa gói, gỡ tài sản khỏi gói...) chặn khi kho đóng hoặc kế hoạch bị khóa,
    /// nhưng VẪN CHO khi dịch vụ đang ân hạn/chờ rà soát (OPLAN-01). Vi phạm → 409 VAULT_NOT_EDITABLE + reason.
    /// Cần vault đã nạp Plan.
    /// </summary>
    public static void EnsureCanReduce(Vault vault)
    {
        // TODO: OPLAN-01 — thao tác giảm khi ân hạn cần đăng nhập lại trong 10 phút; làm khi có Auth.
        VaultNotEditableReason? reason = null;
        if (vault.Status == VaultStatus.Closed)
        {
            reason = VaultNotEditableReason.VaultClosed;
        }
        else if (vault.Plan.Status == PlanStatus.DeathCaseOpen
            || vault.Plan.Status == PlanStatus.HandoverInProgress
            || vault.Plan.Status == PlanStatus.Blocked
            || vault.Plan.Status == PlanStatus.Closed)
        {
            reason = VaultNotEditableReason.PlanLocked;
        }

        if (reason != null)
        {
            Dictionary<string, object?> extensions = new Dictionary<string, object?>();
            extensions["reason"] = reason.Value.ToApiString();
            throw new AppException(AppErrorType.Conflict, ErrorCode.VaultNotEditable,
                "Kho hiện không cho phép chỉnh sửa.", extensions);
        }
    }

    public static VaultNotEditableReason? GetNotEditableReason(Vault vault)
    {
        if (vault.Status == VaultStatus.Closed)
        {
            return VaultNotEditableReason.VaultClosed;
        }
        if (vault.Plan.Status is PlanStatus.DeathCaseOpen or PlanStatus.HandoverInProgress or PlanStatus.Blocked or PlanStatus.Closed)
        {
            return VaultNotEditableReason.PlanLocked;
        }
        if (vault.Entitlement.Status is EntitlementStatus.GraceReadOnly or EntitlementStatus.ExpiredReview)
        {
            return VaultNotEditableReason.ServiceReadOnly;
        }
        return null;
    }
}
