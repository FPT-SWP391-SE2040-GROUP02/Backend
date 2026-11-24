using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;

namespace LegacyVault.Application.Services;

/// <summary>
/// Luật thuần cho việc Admin giao hoặc đổi Người thực thi của một kho (khi chủ kho không chọn được người,
/// hoặc người hiện tại không làm việc). Không đọc DB, không gửi thư.
/// </summary>
public static class ExecutorAssignRules
{
    /// <summary>
    /// Admin giao được khi kho chưa đóng và kế hoạch ở Nháp, đang chạy, hoặc đang bàn giao (người cũ không làm việc được
    /// giữa chừng). Kế hoạch đã đóng thì không đổi nữa.
    /// </summary>
    public static bool PlanAllowsAssign(VaultStatus vaultStatus, PlanStatus planStatus)
    {
        if (vaultStatus == VaultStatus.Closed)
        {
            return false;
        }
        if (planStatus == PlanStatus.Draft || PlanRules.IsRunning(planStatus))
        {
            return true;
        }
        return IsHandoverUnderway(planStatus);
    }

    /// <summary>
    /// Kho đang bàn giao (luật sư đã chấp nhận giấy tờ, chưa đóng): Admin đổi người thì người mới nhận việc ngay, không qua
    /// lời mời; người cũ thôi vai trò trong cùng giao dịch.
    /// </summary>
    public static bool IsHandoverUnderway(PlanStatus planStatus)
    {
        if (planStatus == PlanStatus.HandoverRequested || planStatus == PlanStatus.HandoverInProgress)
        {
            return true;
        }
        return false;
    }

    /// <summary>
    /// Kết quả sắp xếp các dòng Executor hiện có trước khi thêm người mới:
    /// lời mời hết hạn → Expired; lời mời đang chờ hoặc tạm dừng → Revoked; Executor đã chấp nhận khi kế hoạch đang chạy
    /// được giữ lại và người mới sẽ thay (ReplacesExecutorId), người cũ chỉ thôi khi người mới chấp nhận.
    /// Kế hoạch còn Nháp hoặc đang bàn giao mà đã có Executor đã chấp nhận thì thu hồi luôn. Trả id người sẽ bị thay, hoặc null.
    /// </summary>
    public static Guid? PrepareForAssign(IEnumerable<Executor> current, PlanStatus planStatus, DateTimeOffset now)
    {
        Guid? replaces = null;
        foreach (Executor item in current)
        {
            if (item.Status == ExecutorStatus.Invited && item.ExpiresAt <= now)
            {
                item.Status = ExecutorStatus.Expired;
                item.EndedAt = now;
                item.UpdatedAt = now;
            }
            else if (item.Status == ExecutorStatus.Invited || item.Status == ExecutorStatus.Suspended)
            {
                item.Status = ExecutorStatus.Revoked;
                item.EndedAt = now;
                item.UpdatedAt = now;
            }
            else if (item.Status == ExecutorStatus.Accepted)
            {
                if (PlanRules.IsRunning(planStatus))
                {
                    replaces = item.Id;
                }
                else
                {
                    item.Status = ExecutorStatus.Revoked;
                    item.EndedAt = now;
                    item.UpdatedAt = now;
                }
            }
        }
        return replaces;
    }
}
