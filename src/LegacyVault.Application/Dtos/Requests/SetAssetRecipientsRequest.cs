using System.ComponentModel.DataAnnotations;

namespace LegacyVault.Application.Dtos.Requests;

/// <summary>
/// Đặt lại toàn bộ người nhận của một tài sản: danh sách gửi lên là danh sách sau cùng.
/// Nhiều người nghĩa là tài sản được nhận chung; rỗng nghĩa là gỡ gán khỏi tất cả.
/// </summary>
public class SetAssetRecipientsRequest
{
    [Required(ErrorMessage = "Vui lòng gửi danh sách người nhận (có thể rỗng).")]
    [MaxLength(200, ErrorMessage = "Một tài sản chỉ gán được tối đa 200 người nhận.")]
    public List<Guid> BeneficiaryIds { get; set; } = new List<Guid>();

    /// <summary>
    /// Chủ kho đồng ý kế hoạch trở về bản nháp khi thao tác làm kho không còn tài sản nào được gán.
    /// Chưa đồng ý thì backend trả 409 PLAN_DEACTIVATION_CONFIRMATION_REQUIRED.
    /// </summary>
    public bool ConfirmPlanDeactivation { get; set; }
}
