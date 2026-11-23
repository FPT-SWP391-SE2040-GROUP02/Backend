namespace LegacyVault.Application.Dtos.Requests;

/// <summary>
/// Xác nhận khi gỡ gán tài sản khỏi người nhận. Đọc từ QUERY STRING
/// (?confirmPlanDeactivation=true) vì baseService.remove của FE gọi DELETE không có body.
/// </summary>
/// <remarks>
/// Kế hoạch đang chạy mà thao tác làm kho không còn tài sản nào được gán thì backend trả
/// 409 PLAN_DEACTIVATION_CONFIRMATION_REQUIRED. FE hiện cảnh báo; Owner đồng ý thì gọi lại với
/// confirmPlanDeactivation=true → kế hoạch về DRAFT.
/// </remarks>
public class ConfirmPlanImpactRequest
{
    public bool ConfirmPlanDeactivation { get; set; }
}
