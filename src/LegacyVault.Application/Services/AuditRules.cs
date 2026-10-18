using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;

namespace LegacyVault.Application.Services;

/// <summary>
/// Dựng dòng nhật ký hoạt động (SEC-07) dùng chung cho các service. Không truy cập dữ liệu:
/// service tự đưa dòng vào IAuditLogRepository rồi lưu cùng thao tác chính (STATE-02).
/// </summary>
public static class AuditRules
{
    /// <summary>Độ dài tối đa của Reason, khớp cột AuditLogs.Reason.</summary>
    public const int MaxReasonLength = 500;

    /// <summary>
    /// Thao tác thành công của Owner trên kho của mình (SEC-13). targetVersion là phiên bản đối tượng sau thao tác,
    /// ví dụ config_version của kế hoạch khi thao tác đổi cấu hình (SETUP-04, STATE-03); không có thì null.
    /// </summary>
    public static AuditLog OwnerAction(
        Guid vaultId, Guid ownerUserId, AuditAction action, AuditTargetType targetType, Guid? targetId,
        int? targetVersion, DateTimeOffset now)
    {
        return Create(vaultId, ownerUserId, AuditActorRole.Owner, action, targetType, targetId, targetVersion,
            AuditOutcome.Succeeded, null, now);
    }

    /// <summary>
    /// SEC-07: thao tác thành công của Executor hoặc Verifier trên hồ sơ chứng tử. versionNo là số bản liên quan.
    /// reason chỉ nhận mã (ví dụ requestedParts), không nhận lý do tự do vì Owner xem được nhật ký (SEC-13).
    /// </summary>
    public static AuditLog DeathCaseAction(
        Guid vaultId, Guid actorUserId, AuditActorRole actorRole, AuditAction action, Guid caseId,
        int versionNo, string? reason, DateTimeOffset now)
    {
        return Create(vaultId, actorUserId, actorRole, action, AuditTargetType.DeathCase, caseId, versionNo,
            AuditOutcome.Succeeded, reason, now);
    }

    /// <summary>
    /// Dòng nhật ký đầy đủ trường. reason được bỏ khoảng trắng hai đầu và cắt còn tối đa 500 ký tự;
    /// người gọi KHÔNG được truyền mật khẩu, token, số CCCD, tên tài sản hay nội dung file vào reason (SEC-07).
    /// </summary>
    public static AuditLog Create(
        Guid? vaultId, Guid? actorUserId, AuditActorRole actorRole, AuditAction action,
        AuditTargetType targetType, Guid? targetId, int? targetVersion, AuditOutcome outcome,
        string? reason, DateTimeOffset now)
    {
        AuditLog log = new AuditLog();
        log.VaultId = vaultId;
        log.ActorUserId = actorUserId;
        log.ActorRole = actorRole;
        log.Action = action;
        log.TargetType = targetType;
        log.TargetId = targetId;
        log.TargetVersion = targetVersion;
        log.Outcome = outcome;
        log.Reason = NormalizeReason(reason);
        log.At = now;
        return log;
    }

    /// <summary>Chuỗi rỗng hoặc chỉ khoảng trắng → null; dài quá thì cắt còn MaxReasonLength ký tự.</summary>
    public static string? NormalizeReason(string? reason)
    {
        string? text = TextInput.NullIfBlank(reason);
        if (text == null)
        {
            return null;
        }
        if (text.Length > MaxReasonLength)
        {
            return text.Substring(0, MaxReasonLength);
        }
        return text;
    }
}
