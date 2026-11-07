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
    /// Admin chỉ giao được khi kho chưa đóng và kế hoạch còn ở Nháp hoặc đang chạy điểm danh.
    /// Từ lúc nộp giấy chứng tử trở đi, Người thực thi của hồ sơ không đổi nữa.
    /// </summary>
    public static bool PlanAllowsAssign(VaultStatus vaultStatus, PlanStatus planStatus)
    {
        if (vaultStatus == VaultStatus.Closed)
        {
            return false;
        }
        return planStatus == PlanStatus.Draft || PlanRules.IsRunning(planStatus);
    }

    /// <summary>
    /// Kết quả sắp xếp các dòng Executor hiện có trước khi thêm người mới:
    /// lời mời hết hạn → Expired; lời mời đang chờ hoặc tạm dừng → Revoked; Executor đã chấp nhận khi kế hoạch đang chạy
    /// được giữ lại và người mới sẽ thay (ReplacesExecutorId), người cũ chỉ thôi khi người mới chấp nhận.
    /// Kế hoạch còn Nháp mà đã có Executor đã chấp nhận thì thu hồi luôn. Trả id người sẽ bị thay, hoặc null.
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
