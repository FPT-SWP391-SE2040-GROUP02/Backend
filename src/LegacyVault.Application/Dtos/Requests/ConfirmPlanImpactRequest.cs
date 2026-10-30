namespace LegacyVault.Application.Dtos.Requests;

/// <summary>
/// Xác nhận khi xóa gói / gỡ tài sản khỏi gói. Đọc từ QUERY STRING
/// (?confirmPlanDeactivation=true) vì baseService.remove của FE gọi DELETE không có body.
/// </summary>
/// <remarks>
/// SETUP-16: kế hoạch đang chạy mà thao tác làm kho không còn gói READY nào thì backend trả
/// 409 PLAN_DEACTIVATION_CONFIRMATION_REQUIRED. FE hiện cảnh báo; Owner đồng ý thì gọi lại với
/// confirmPlanDeactivation=true → kế hoạch về DRAFT và dừng lịch điểm danh.
/// </remarks>
public class ConfirmPlanImpactRequest
{
    public bool ConfirmPlanDeactivation { get; set; }
}
