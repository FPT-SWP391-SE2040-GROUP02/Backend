using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;

namespace LegacyVault.Application.Services;

/// <summary>Luật nghiệp vụ của kế hoạch bàn giao (HandoverPlan) dùng chung cho các service.</summary>
public static class PlanRules
{
    /// <summary>Kế hoạch đang chạy: đã kích hoạt và lịch điểm danh đang chạy (SETUP-16, §11.2).</summary>
    public static bool IsRunning(PlanStatus status)
    {
        if (status == PlanStatus.Active
            || status == PlanStatus.CheckinOverdue
            || status == PlanStatus.AwaitingCheck)
        {
            return true;
        }
        return false;
    }

    /// <summary>SETUP-04: mọi thay đổi cấu hình kế hoạch (gói, tài sản trong gói, người nhận...) tăng ConfigVersion.</summary>
    public static void BumpConfigVersion(HandoverPlan plan, DateTimeOffset now)
    {
        plan.ConfigVersion = plan.ConfigVersion + 1;
        plan.UpdatedAt = now;
    }

    /// <summary>SETUP-16: kế hoạch mất gói READY cuối cùng (Owner đã xác nhận) → về DRAFT.</summary>
    public static void Deactivate(HandoverPlan plan, DateTimeOffset now)
    {
        plan.Status = PlanStatus.Draft;
        plan.UpdatedAt = now;
        // TODO: dừng lịch điểm danh và chuyển Executor sang SUSPENDED khi có module điểm danh/Executor (§11.2).
    }
}
