using LegacyVault.Application.Repositories;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;

namespace LegacyVault.Application.Services;

/// <summary>
/// Ghi nhật ký cho sự kiện xác thực (đăng nhập, 2FA, mật khẩu). Chỉ đánh dấu INSERT; dòng được lưu cùng
/// lần SaveChanges của thao tác chính nên rollback thì dòng cũng mất (STATE-02). Không gắn kho (VaultId null).
/// </summary>
/// <remarks>repo null (test cũ hoặc nơi chưa nối nhật ký) thì bỏ qua. reason chỉ nhận mã ngắn như PASSWORD, GOOGLE (SEC-07).</remarks>
public static class AuthAudit
{
    public static void Record(
        IAuditLogRepository? repository, User user, AuditAction action, AuditOutcome outcome,
        string? reason, DateTimeOffset now)
    {
        if (repository == null)
        {
            return;
        }
        repository.Add(AuditRules.Create(
            null, user.Id, ActorRoleOf(user.Role), action, AuditTargetType.User, user.Id, null, outcome, reason, now));
    }

    public static AuditActorRole ActorRoleOf(UserRole role)
    {
        return role switch
        {
            UserRole.Admin => AuditActorRole.Admin,
            UserRole.Verifier => AuditActorRole.Verifier,
            UserRole.Executor => AuditActorRole.Executor,
            _ => AuditActorRole.Owner,
        };
    }
}
